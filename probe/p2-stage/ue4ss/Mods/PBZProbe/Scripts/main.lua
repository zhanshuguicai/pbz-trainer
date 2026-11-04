-- PBZProbe v2: read-only discovery and sampling for health, flask count and combat state.
-- Never writes properties; only calls const query functions (e.g. GetGameplayTagCount).
-- Output goes to UE4SS.log with a [PBZProbe] prefix.
-- F8: run adhoc.lua once.  F9: dump player objects.  F10: toggle sampling.  F11: reload watch.lua.
-- v1 crashed inside UE4SS.dll while sampling with LoopAsync + FindAllOf every 100 ms; v2 samples with
-- LoopInGameThreadWithDelay and never searches all objects inside the loop.

local UEHelpers = require("UEHelpers")

local TAG = "[PBZProbe] "
local SCRIPT_DIR = string.match(debug.getinfo(1, "S").source, "^@?(.*)[\\/][^\\/]+$") or "."
local WATCH_FILE = SCRIPT_DIR .. "/../watch.lua"
local ADHOC_FILE = SCRIPT_DIR .. "/../adhoc.lua"
local SAMPLE_MS = 200
local KEYWORDS = { "health", "hp", "life", "flask", "potion", "heal", "medic", "elixir", "drink", "gourd",
    "combat", "battle", "fight", "alert", "aggro", "hatred", "threat", "state", "item", "inventor", "bag",
    "count", "stack", "num", "quant", "money", "coin", "gold", "currency", "material", "dust", "prop",
    "dead", "death", "alive" }

local function log(fmt, ...) print(TAG .. string.format(fmt, ...) .. "\n") end

local function safe(fn, ...)
    local ok, res = pcall(fn, ...)
    if ok then return res end
    return nil
end

local function isValid(obj) return obj ~= nil and safe(function() return obj:IsValid() end) == true end
local function fullName(obj) return safe(function() return obj:GetFullName() end) or "?" end
local function fname(prop) return safe(function() return prop:GetFName():ToString() end) or "?" end
local function propType(prop) return safe(function() return prop:GetClass():GetFName():ToString() end) or "?" end
local function addr(obj) return safe(function() return obj:GetAddress() end) end

local function matchesKeyword(name)
    local lower = string.lower(name)
    for _, k in ipairs(KEYWORDS) do if string.find(lower, k, 1, true) then return true end end
    return false
end

-- Walks the class and its supers, calling fn(prop) for each property.
local function forEachPropertyDeep(obj, fn)
    local cls = safe(function() return obj:GetClass() end)
    local guard = 0
    while isValid(cls) and guard < 64 do
        safe(function() cls:ForEachProperty(fn) end)
        cls = safe(function() return cls:GetSuperStruct() end)
        guard = guard + 1
    end
end

local function classChain(obj)
    local names = {}
    local cls = safe(function() return obj:GetClass() end)
    local guard = 0
    while isValid(cls) and guard < 64 do
        table.insert(names, safe(function() return cls:GetFName():ToString() end) or "?")
        cls = safe(function() return cls:GetSuperStruct() end)
        guard = guard + 1
    end
    return table.concat(names, " < ")
end

local SCALARS = { FloatProperty = true, DoubleProperty = true, IntProperty = true, Int64Property = true,
    Int16Property = true, Int8Property = true, UInt32Property = true, UInt16Property = true,
    UInt64Property = true, ByteProperty = true, BoolProperty = true }

-- Formats a property value; struct values are expanded `depth` levels, arrays show up to `arrayItems` elements.
local function valueString(container, prop, depth, arrayItems)
    local name = fname(prop)
    local t = propType(prop)
    arrayItems = arrayItems or 0
    if SCALARS[t] then
        return tostring(safe(function() return container[name] end))
    elseif t == "EnumProperty" then
        local v = safe(function() return container[name] end)
        local en = safe(function() return prop:GetEnum():GetNameByValue(v):ToString() end)
        return string.format("%s(%s)", tostring(en), tostring(v))
    elseif t == "NameProperty" or t == "StrProperty" or t == "TextProperty" then
        return tostring(safe(function() return container[name]:ToString() end))
    elseif t == "ObjectProperty" or t == "WeakObjectProperty" or t == "ClassProperty" then
        local v = safe(function() return container[name] end)
        if t == "WeakObjectProperty" then v = safe(function() return v:Get() end) end
        if isValid(v) then return fullName(v) end
        return "nil"
    elseif t == "ArrayProperty" then
        local v = safe(function() return container[name] end)
        local n = safe(function() return v:GetArrayNum() end) or 0
        local inner = safe(function() return prop:GetInner() end)
        local innerType = propType(inner)
        local innerStruct = safe(function() return inner:GetStruct() end)
        local head = string.format("Array<%s%s> n=%s", innerType,
            innerStruct and (":" .. (safe(function() return innerStruct:GetFName():ToString() end) or "?")) or "", tostring(n))
        if arrayItems > 0 and n > 0 then
            local items = {}
            safe(function()
                v:ForEach(function(index, elem)
                    if #items >= arrayItems then return true end
                    local e = elem:get()
                    if innerStruct then
                        local parts = {}
                        safe(function()
                            innerStruct:ForEachProperty(function(p)
                                local s = valueString(e, p, depth - 1, 0)
                                if s and #parts < 16 then table.insert(parts, fname(p) .. "=" .. s) end
                            end)
                        end)
                        table.insert(items, string.format("[%d]{%s}", index - 1, table.concat(parts, ", ")))
                    elseif isValid(e) then
                        table.insert(items, string.format("[%d]%s", index - 1, fullName(e)))
                    else
                        table.insert(items, string.format("[%d]%s", index - 1, tostring(e)))
                    end
                end)
            end)
            return head .. " " .. table.concat(items, " ")
        end
        return head
    elseif t == "StructProperty" then
        local sname = safe(function() return prop:GetStruct():GetFName():ToString() end) or "?"
        if depth > 0 then
            local v = safe(function() return container[name] end)
            local parts = {}
            if v ~= nil then
                safe(function()
                    prop:GetStruct():ForEachProperty(function(inner)
                        local s = valueString(v, inner, depth - 1, arrayItems)
                        if s ~= nil and #parts < 16 then table.insert(parts, fname(inner) .. "=" .. s) end
                    end)
                end)
            end
            return string.format("%s{%s}", sname, table.concat(parts, ", "))
        end
        return sname
    end
    return nil
end

local function dumpObject(label, obj, filtered, arrayItems)
    if not isValid(obj) then log("%s: <invalid>", label) return end
    log("== %s: %s", label, fullName(obj))
    log("   class: %s", classChain(obj))
    local count, shown = 0, 0
    forEachPropertyDeep(obj, function(prop)
        count = count + 1
        local name = fname(prop)
        if filtered and not matchesKeyword(name) then return end
        if shown < 400 then log("   %s %s = %s", propType(prop), name, tostring(valueString(obj, prop, 2, arrayItems))) end
        shown = shown + 1
    end)
    log("   (%d properties, %d shown)", count, shown)
end

local function getPlayer()
    local pc = UEHelpers.GetPlayerController()
    if not isValid(pc) then return nil, nil end
    local pawn = safe(function() return pc.Pawn end)
    if not isValid(pawn) then pawn = nil end
    return pc, pawn
end

local tagNames = {}
local function tagCount(pawn, tag)
    local asc = safe(function() return pawn.AbilitySystemComponent end)
    if not isValid(asc) then return nil end
    tagNames[tag] = tagNames[tag] or FName(tag)
    return safe(function() return asc:GetGameplayTagCount({ TagName = tagNames[tag] }) end)
end

-- Shared with adhoc.lua and watch.lua.
PBZProbe = { log = log, safe = safe, isValid = isValid, fullName = fullName, fname = fname, propType = propType,
    addr = addr, dumpObject = dumpObject, valueString = valueString, classChain = classChain,
    forEachPropertyDeep = forEachPropertyDeep, getPlayer = getPlayer, tagCount = tagCount, UEHelpers = UEHelpers }

local function discover()
    log("######## DISCOVERY START ########")
    local pc, pawn = getPlayer()
    if not pc then log("no valid PlayerController") return end
    log("world: %s", fullName(UEHelpers.GetWorld()))
    dumpObject("Pawn(filtered)", pawn, true, 0)
    if pawn then
        dumpObject("ActorStateComponent", safe(function() return pawn["Actor State Component"] end), false, 4)
        dumpObject("AttributeComponent", safe(function() return pawn["Attribute Component"] end), false, 4)
        dumpObject("DeathComponent", safe(function() return pawn["Death Component"] end), false, 4)
        log("tags: InCombat=%s Dead=%s", tostring(tagCount(pawn, "CharacterState.InCombat")), tostring(tagCount(pawn, "CharacterState.Dead")))
    end
    dumpObject("CombatTelemetry", safe(function() return pc.CombatTelemetry end), false, 4)
    dumpObject("BackPackSubsystem", safe(function() return FindFirstOf("PBZBackPackSubsystem") end), false, 8)
    log("######## DISCOVERY END ########")
end

-- ---------- sampling ----------
-- watch.lua returns a list of { label = "...", fn = function(ctx) return value end }, ctx = { pc, pawn, P = PBZProbe }.
local watch = {}
local sampling = false
local lastLine = nil
local lastBeat = 0
local seq = 0

local function loadWatch()
    local ok, res = pcall(dofile, WATCH_FILE)
    if ok and type(res) == "table" then
        watch = res
        log("watch loaded: %d entries", #watch)
    else
        log("watch load failed: %s", tostring(res))
    end
end

local function sampleOnce()
    local pc, pawn = getPlayer()
    local ctx = { pc = pc, pawn = pawn, P = PBZProbe }
    local parts = { "pawn=" .. (pawn and string.format("%x", addr(pawn) or 0) or "nil") }
    for _, w in ipairs(watch) do
        local ok, v = pcall(w.fn, ctx)
        if not ok or v == nil then v = "unknown" end
        if type(v) == "number" then v = string.format("%.3f", v) end
        table.insert(parts, w.label .. "=" .. tostring(v))
    end
    local line = table.concat(parts, " | ")
    local now = os.clock()
    if line ~= lastLine or now - lastBeat > 5 then
        seq = seq + 1
        log("S %d t=%.2f %s%s", seq, now, line, line == lastLine and " (beat)" or "")
        lastLine = line
        lastBeat = now
    end
end

loadWatch()

-- Key callbacks only flip flags; all UObject work happens in the game-thread loop below.
local pendingDiscover, pendingAdhoc = false, false
RegisterKeyBind(Key.F8, function() pendingAdhoc = true end)
RegisterKeyBind(Key.F9, function() pendingDiscover = true end)
RegisterKeyBind(Key.F10, function() sampling = not sampling log("sampling %s", sampling and "ON" or "OFF") end)
RegisterKeyBind(Key.F11, function() loadWatch() end)

LoopInGameThreadWithDelay(SAMPLE_MS, function()
    if pendingDiscover then
        pendingDiscover = false
        local ok, err = pcall(discover)
        if not ok then log("discover error: %s", tostring(err)) end
    end
    if pendingAdhoc then
        pendingAdhoc = false
        log("adhoc start")
        local ok, err = pcall(dofile, ADHOC_FILE)
        log("adhoc end %s", ok and "ok" or ("error: " .. tostring(err)))
    end
    if sampling then
        local ok, err = pcall(sampleOnce)
        if not ok then log("sample error: %s", tostring(err)) end
    end
end)

log("v2 loaded (read-only). F8 adhoc, F9 discover, F10 sampling, F11 reload watch")
