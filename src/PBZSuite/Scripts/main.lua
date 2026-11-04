-- PBZSuite: the in-game half of the PBZ mod suite. See docs/adr/0001-architecture.md.
-- Telemetry snapshots to runtime/telemetry-<session>.jsonl and config.json polling (T4a); telemetry is read-only.
-- T8 value lock: holds Health / Shachi / UltimateCharge / SpiritShadow at their max or at a fixed value chosen in
-- the manager ("数值修改" page), while the manager turns it on.
-- Boss telemetry: the HUD's boss health bar (WB_BossHPBar_C) with the boss's name and health, for the condition
-- player's boss triggers.
-- T5a items: on request writes runtime/inventory.json (money and every inventory entry) and applies amount edits
-- for money and upgrade/progression materials through the game's own functions.
-- T5b auto deflect / dodge: watches incoming attack frames on the player's DeflectComponent and, a moment before each
-- hit, asks the manager (runtime/parry-cmd.txt) to press the real deflect or dodge key (IsDodge frames get a dodge).
-- Injected Enhanced Input and direct ability activation were tried first and do nothing in this game (evidence/t5b).
-- Writing is refused unless the manager has set compatible=true for this game build.

local UEHelpers = require("UEHelpers")

local SUITE_VERSION = "1.0.1"
local SCHEMA_VERSION = 1
local GAME_BUILD = "26367565"
local SAMPLE_MS = 200
local CONFIG_POLL_MS = 400
local ROTATE_BYTES = 8 * 1024 * 1024
local SCRIPT_DIR = string.match(debug.getinfo(1, "S").source, "^@?(.*)[\\/][^\\/]+$") or "."
local RUNTIME_DIR = SCRIPT_DIR .. "/../runtime"
local CONFIG_FILE = RUNTIME_DIR .. "/config.json"
local INVENTORY_FILE = RUNTIME_DIR .. "/inventory.json"
local PARRY_CMD_FILE = RUNTIME_DIR .. "/parry-cmd.txt"

local function log(fmt, ...) print("[PBZSuite] " .. string.format(fmt, ...) .. "\n") end

local function safe(fn, ...)
    local ok, res = pcall(fn, ...)
    if ok then return res end
    return nil
end

local function isValid(obj) return obj ~= nil and safe(function() return obj:IsValid() end) == true end

-- ---------- JSON ----------
local NULL = setmetatable({}, { __tostring = function() return "null" end })

local function encodeString(s)
    return '"' .. s:gsub('[%c"\\]', function(c)
        if c == '"' then return '\\"' elseif c == "\\" then return "\\\\"
        elseif c == "\n" then return "\\n" elseif c == "\r" then return "\\r" elseif c == "\t" then return "\\t" end
        return string.format("\\u%04x", c:byte())
    end) .. '"'
end

local encode
encode = function(v)
    local t = type(v)
    if v == nil or v == NULL then return "null"
    elseif t == "boolean" then return v and "true" or "false"
    elseif t == "number" then
        if v ~= v or v == math.huge or v == -math.huge then return "null" end
        if math.type(v) == "integer" then return tostring(v) end
        return (string.format("%.4f", v):gsub("0+$", ""):gsub("%.$", ""))
    elseif t == "string" then return encodeString(v)
    elseif t == "table" then
        -- Empty tables are only used for lists (errors), so they encode as [].
        if #v > 0 or next(v) == nil then
            local parts = {}
            for i = 1, #v do parts[i] = encode(v[i]) end
            return "[" .. table.concat(parts, ",") .. "]"
        end
        local keys = {}
        for k in pairs(v) do table.insert(keys, k) end
        table.sort(keys)
        local parts = {}
        for _, k in ipairs(keys) do table.insert(parts, encodeString(tostring(k)) .. ":" .. encode(v[k])) end
        return "{" .. table.concat(parts, ",") .. "}"
    end
    return "null"
end

-- Minimal decoder for config.json written by the manager. Returns nil, err on malformed input.
local function decode(text)
    local pos = 1
    local function skip() pos = text:find("[^ \t\r\n]", pos) or (#text + 1) end
    local value
    local function err(msg) error(string.format("json %s at %d", msg, pos), 0) end
    local function str()
        local out, i = {}, pos + 1
        while true do
            local c = text:sub(i, i)
            if c == "" then err("unterminated string") end
            if c == '"' then pos = i + 1 return table.concat(out) end
            if c == "\\" then
                local n = text:sub(i + 1, i + 1)
                local map = { ['"'] = '"', ["\\"] = "\\", ["/"] = "/", b = "\b", f = "\f", n = "\n", r = "\r", t = "\t" }
                if map[n] then table.insert(out, map[n]) i = i + 2
                elseif n == "u" then
                    table.insert(out, utf8.char(tonumber(text:sub(i + 2, i + 5), 16) or 63)) i = i + 6
                else err("bad escape") end
            else table.insert(out, c) i = i + 1 end
        end
    end
    value = function()
        skip()
        local c = text:sub(pos, pos)
        if c == "{" then
            local obj = {}
            pos = pos + 1 skip()
            if text:sub(pos, pos) == "}" then pos = pos + 1 return obj end
            while true do
                skip()
                if text:sub(pos, pos) ~= '"' then err("expected key") end
                local k = str() skip()
                if text:sub(pos, pos) ~= ":" then err("expected colon") end
                pos = pos + 1
                obj[k] = value() skip()
                local d = text:sub(pos, pos) pos = pos + 1
                if d == "}" then return obj elseif d ~= "," then err("expected comma") end
            end
        elseif c == "[" then
            local arr = {}
            pos = pos + 1 skip()
            if text:sub(pos, pos) == "]" then pos = pos + 1 return arr end
            while true do
                table.insert(arr, value()) skip()
                local d = text:sub(pos, pos) pos = pos + 1
                if d == "]" then return arr elseif d ~= "," then err("expected comma") end
            end
        elseif c == '"' then return str()
        elseif text:sub(pos, pos + 3) == "true" then pos = pos + 4 return true
        elseif text:sub(pos, pos + 4) == "false" then pos = pos + 5 return false
        elseif text:sub(pos, pos + 3) == "null" then pos = pos + 4 return NULL
        else
            local num = text:match("^-?%d+%.?%d*[eE]?[-+]?%d*", pos)
            if not num or num == "" then err("unexpected token") end
            pos = pos + #num
            return tonumber(num)
        end
    end
    local ok, res = pcall(function() local v = value() skip() if pos <= #text then err("trailing data") end return v end)
    if ok then return res end
    return nil, res
end

-- ---------- config ----------
-- Everything starts off; the manager turns modules on. `compatible` is set by the manager after it checks
-- the game build and EXE hash; write-capable modules must refuse to run without it.
-- value: -1 = refill to max, otherwise hold this number (capped at the max).
local LOCK_OFF = { enabled = false, health = false, shachi = false, ultimate = false, spirit = false,
    value = { health = -1, shachi = -1, ultimate = -1, spirit = -1 } }
local config = { revision = 0, compatible = false, modules = { parry = false, items = false }, lock = LOCK_OFF,
    inventoryRequest = 0, itemOps = {},
    parry = { deflect = true, dodge = true, leadMs = 120 } }
local lastConfigText = nil

local function pollConfig()
    local f = io.open(CONFIG_FILE, "rb")
    if not f then return end
    local text = f:read("a")
    f:close()
    if text == lastConfigText then return end
    lastConfigText = text
    local parsed, err = decode(text)
    if type(parsed) ~= "table" or type(parsed.revision) ~= "number" then
        log("config rejected: %s", tostring(err or "missing revision"))
        return
    end
    local modules = type(parsed.modules) == "table" and parsed.modules or {}
    config = {
        revision = parsed.revision,
        compatible = parsed.compatible == true,
        modules = { parry = modules.parry == true, items = modules.items == true },
        lock = LOCK_OFF,
        inventoryRequest = type(parsed.inventoryRequest) == "number" and parsed.inventoryRequest or 0,
        itemOps = {},
        parry = { deflect = true, dodge = true, leadMs = 120 },
    }
    if type(parsed.parry) == "table" then
        local pr = parsed.parry
        config.parry = { deflect = pr.deflect ~= false, dodge = pr.dodge ~= false,
            leadMs = type(pr.leadMs) == "number" and math.max(0, math.min(300, pr.leadMs)) or 120 }
    end
    -- Item edits: { seq, kind = "item" | "money", id, target }. Applied once each, in seq order.
    if type(parsed.itemOps) == "table" then
        for _, op in ipairs(parsed.itemOps) do
            if type(op) == "table" and type(op.seq) == "number" and type(op.target) == "number" then
                table.insert(config.itemOps, { seq = op.seq, kind = op.kind, id = op.id, target = op.target })
            end
        end
        table.sort(config.itemOps, function(a, b) return a.seq < b.seq end)
    end
    if type(parsed.lock) == "table" then
        local l = parsed.lock
        local function num(v) if type(v) == "number" then return v end return -1 end
        config.lock = { enabled = l.enabled == true, health = l.health == true, shachi = l.shachi == true,
            ultimate = l.ultimate == true, spirit = l.spirit == true,
            value = { health = num(l.healthValue), shachi = num(l.shachiValue), ultimate = num(l.ultimateValue), spirit = num(l.spiritValue) } }
    end
    local l = config.lock
    local function item(k) return l[k] and (l.value[k] < 0 and "max" or tostring(l.value[k])) or "off" end
    log("config revision %d applied (compatible=%s, lock=%s hp=%s shachi=%s ult=%s spirit=%s)", config.revision,
        tostring(config.compatible), tostring(l.enabled), item("health"), item("shachi"), item("ultimate"), item("spirit"))
end

-- ---------- telemetry output ----------
math.randomseed(os.time())
local sessionId = string.format("%08x%04x", os.time(), math.random(0, 0xffff))
local sequence = 0
local out, outBytes, outPart = nil, 0, 0
local lastOpenFailure = -math.huge

local function openOutput()
    if os.clock() - lastOpenFailure < 5 then return false end
    local path = string.format("%s/telemetry-%s-%d.jsonl", RUNTIME_DIR, sessionId, outPart)
    out = io.open(path, "ab")
    if not out then
        lastOpenFailure = os.clock()
        log("cannot open %s", path)
        return false
    end
    outBytes = out:seek("end") or 0
    log("telemetry -> %s", path)
    return true
end

local function writeLine(record)
    if not out and not openOutput() then return end
    local line = encode(record) .. "\n"
    local ok = out:write(line)
    if not ok then
        out:close() out = nil
        lastOpenFailure = os.clock()
        return
    end
    out:flush()
    outBytes = outBytes + #line
    if outBytes >= ROTATE_BYTES then
        out:close() out = nil
        outPart = outPart + 1
    end
end

-- ---------- sampling ----------
local backpack = nil
local lastPawnAddr = nil
local lockActive = false -- set each tick by the lock module below; reported in telemetry
local parryActive = false -- set each tick by the auto deflect module below; reported in telemetry
local parry = { pawnAddr = nil, comp = nil, handled = {}, pending = {}, lastPress = -1,
    deflects = 0, dodges = 0, hitsTaken = 0, cleanHits = 0 }
local playerGeneration = 0
local tagNames = {}

local function tagCount(asc, tag)
    tagNames[tag] = tagNames[tag] or FName(tag)
    local n = safe(function() return asc:GetGameplayTagCount({ TagName = tagNames[tag] }) end)
    if type(n) == "number" then return n end
    return nil
end

local function readHealth(pawn)
    local cur = safe(function() return pawn.AttributeSetBase.Health.CurrentValue end)
    local max = safe(function() return pawn.AttributeSetBase.Health_Max.CurrentValue end)
    if type(cur) == "number" and type(max) == "number" and max > 0 then
        return { current = cur, max = max, valid = true }
    end
    return { current = NULL, max = NULL, valid = false }
end

-- Shachi / UltimateCharge / SpiritShadow with their max, for the manager's value page. Same shape as health.
local function readAttrPair(pawn, attr, maxAttr)
    local cur = safe(function() return pawn.AttributeSetBase[attr].CurrentValue end)
    local max = safe(function() return pawn.AttributeSetBase[maxAttr].CurrentValue end)
    if type(cur) == "number" and type(max) == "number" then
        return { current = cur, max = max, valid = true }
    end
    return { current = NULL, max = NULL, valid = false }
end

local function readCombat(pawn)
    local asc = safe(function() return pawn.AbilitySystemComponent end)
    if not isValid(asc) then return { inCombat = NULL, dead = NULL, valid = false } end
    local inCombat = tagCount(asc, "CharacterState.InCombat")
    local dead = tagCount(asc, "CharacterState.Dead")
    if inCombat == nil or dead == nil then return { inCombat = NULL, dead = NULL, valid = false } end
    return { inCombat = inCombat, dead = dead, valid = true }
end

-- Rule 3b: an empty slot (None) counts 0; an id missing from MedicineInfos makes the flask state unknown.
local function readSlot(bp, slotProp)
    local id = safe(function() return bp[slotProp]:ToString() end)
    if type(id) ~= "string" then return { id = NULL, count = NULL, valid = false } end
    if id == "None" or id == "" then return { id = "None", count = 0, valid = true } end
    local count
    safe(function()
        bp.MedicineInfos:ForEach(function(key, value)
            if key:get():ToString() == id then
                count = value:get().CurrentTime
                return true
            end
        end)
    end)
    if type(count) ~= "number" then return { id = id, count = NULL, valid = false } end
    return { id = id, count = count, valid = true }
end

local function readFlask()
    if not isValid(backpack) then backpack = safe(function() return FindFirstOf("PBZBackPackSubsystem") end) end
    if not isValid(backpack) then
        backpack = nil
        return { top = NULL, bottom = NULL, total = NULL, valid = false }
    end
    local top = readSlot(backpack, "TopMedicineID")
    local bottom = readSlot(backpack, "BottomMedicineID")
    local valid = top.valid and bottom.valid
    return { top = top, bottom = bottom, total = valid and (top.count + bottom.count) or NULL, valid = valid }
end

-- ---------- boss (HUD health bar) ----------
-- Probe 2026-11-04 (evidence/boss/round3): the boss bar is a WB_BossHPBar_C widget inside the HUD layout's widget
-- tree; its Character is the boss and BossName its display name. UPBZHUDLayout.RegisterBossHandles stays empty, so
-- it is not used. A visible bar with a valid Character = a boss fight.
local bossBar, barLookupAt = nil, -10
local lastBossKey, lastBossActive = nil, false

local function findBar()
    if isValid(bossBar) then return bossBar end
    -- One FindFirstOf at most every 2 s, never a FindAllOf (see crash notes in HANDOFF).
    if os.clock() - barLookupAt < 2 then return nil end
    barLookupAt = os.clock()
    local w = safe(function() return FindFirstOf("WB_BossHPBar_C") end)
    local name = w and safe(function() return w:GetFullName() end) or ""
    if isValid(w) and not string.find(name, "Default__", 1, true) then bossBar = w end
    return isValid(bossBar) and bossBar or nil
end

local function readBoss()
    local bar = findBar()
    if not bar then return { active = false, count = 0, valid = false } end
    local visible = safe(function() return bar:IsVisible() end) == true
    local ch = safe(function() return bar.Character end)
    if not visible or not isValid(ch) then
        if lastBossActive then log("boss bar hidden") end
        lastBossActive, lastBossKey = false, nil
        return { active = false, count = 0, valid = true }
    end
    local cur = safe(function() return ch.AttributeSetBase.Health.CurrentValue end)
    local max = safe(function() return ch.AttributeSetBase.Health_Max.CurrentValue end)
    local key = safe(function() return ch:GetAddress() end)
    local name = safe(function() return bar.BossName:GetText():ToString() end)
    if not name or name == "" then name = safe(function() return ch:GetClass():GetFName():ToString() end) end
    if key ~= lastBossKey then
        lastBossKey = key
        log("boss bar: %s (%s) hp %s/%s", tostring(name), safe(function() return ch:GetClass():GetFName():ToString() end) or "?",
            tostring(cur), tostring(max))
    end
    lastBossActive = true
    return { active = true, count = 1, id = tostring(key), name = name or NULL,
        current = type(cur) == "number" and cur or NULL, max = type(max) == "number" and max or NULL,
        valid = type(cur) == "number" and type(max) == "number" and max > 0 }
end

local function sample()
    local record = {
        schemaVersion = SCHEMA_VERSION,
        suiteVersion = SUITE_VERSION,
        sessionId = sessionId,
        gameBuild = GAME_BUILD,
        configRevision = config.revision,
        modules = { telemetry = true, parry = parryActive, items = config.modules.items, lock = lockActive },
        parry = { deflects = parry.deflects, dodges = parry.dodges, hitsTaken = parry.hitsTaken, clean = parry.cleanHits },
        errors = {},
    }
    sequence = sequence + 1
    record.sequence = sequence
    record.monotonicMs = math.floor(os.clock() * 1000)
    record.wallTime = os.time()

    local pc = UEHelpers.GetPlayerController()
    local pawn = isValid(pc) and safe(function() return pc.Pawn end) or nil
    if not isValid(pawn) then
        record.lifecycle = "noPlayer"
        record.playerGeneration = playerGeneration
        record.health = { current = NULL, max = NULL, valid = false }
        record.combat = { inCombat = NULL, dead = NULL, valid = false }
        record.flask = { top = NULL, bottom = NULL, total = NULL, valid = false }
        return record
    end
    local addr = safe(function() return pawn:GetAddress() end)
    if addr ~= lastPawnAddr then
        lastPawnAddr = addr
        playerGeneration = playerGeneration + 1
    end
    record.lifecycle = "playing"
    record.playerGeneration = playerGeneration
    record.health = readHealth(pawn)
    local bok, boss = pcall(readBoss)
    record.boss = bok and boss or { active = false, count = 0, valid = false }
    record.values = {
        shachi = readAttrPair(pawn, "Shachi", "Shachi_Max"),
        ultimate = readAttrPair(pawn, "UltimateCharge", "UltimateCharge_Max"),
        spirit = readAttrPair(pawn, "SpiritShadow", "SpiritShadow_Max"),
    }
    record.combat = readCombat(pawn)
    record.flask = readFlask()
    return record
end

-- ---------- lock (T8) ----------
-- Probe result (docs/worklog/2026-11-04-t8-lock-probe-claude.md): writing BaseValue and CurrentValue sticks for all
-- four; Health also needs OnRep_Health(old) so GAS broadcasts the change and the health bar refreshes.
local LOCK_ITEMS = {
    { key = "health", attr = "Health", max = "Health_Max", onrep = "OnRep_Health" },
    { key = "shachi", attr = "Shachi", max = "Shachi_Max" },
    { key = "ultimate", attr = "UltimateCharge", max = "UltimateCharge_Max" },
    { key = "spirit", attr = "SpiritShadow", max = "SpiritShadow_Max" },
}
-- After a new Pawn appears its first samples carry base values (850 / 977.5); wait before writing.
local LOCK_SETTLE_MS = 1000
local lockPawnAddr, lockSettleMs, lockErrors = nil, 0, 0

local function lockWanted()
    local l = config.lock
    return config.compatible and l.enabled and (l.health or l.shachi or l.ultimate or l.spirit)
end

local function readValue(set, name)
    local a = safe(function() return set[name] end)
    if a == nil then return nil, nil end
    return safe(function() return a.BaseValue end), safe(function() return a.CurrentValue end)
end

local function lockTick()
    if not lockWanted() then return end
    local pc = UEHelpers.GetPlayerController()
    local pawn = isValid(pc) and safe(function() return pc.Pawn end) or nil
    if not isValid(pawn) then lockPawnAddr = nil return end
    local addr = safe(function() return pawn:GetAddress() end)
    if addr ~= lockPawnAddr then
        lockPawnAddr = addr
        lockSettleMs = 0
    end
    if lockSettleMs < LOCK_SETTLE_MS then
        lockSettleMs = lockSettleMs + SAMPLE_MS
        return
    end
    local set = safe(function() return pawn.AttributeSetBase end)
    if not isValid(set) then return end
    local asc = safe(function() return pawn.AbilitySystemComponent end)
    for _, it in ipairs(LOCK_ITEMS) do
        if config.lock[it.key] then
            local oldBase, cur = readValue(set, it.attr)
            local _, max = readValue(set, it.max)
            local target = config.lock.value[it.key]
            if max and (target < 0 or target > max) then target = max end
            -- Full mode only tops up; fixed mode also pulls a higher value back down.
            local differs = cur and max and max > 0 and (cur < target - 0.001 or (config.lock.value[it.key] >= 0 and cur > target + 0.001))
            if differs then
                -- Never pull a dead or dying character back: death flow must run as the game intends.
                local dead = it.key == "health" and (cur <= 0 or (tagCount(asc, "CharacterState.Dead") or 0) > 0)
                if not dead then
                    local ok, err = pcall(function()
                        local a = set[it.attr]
                        a.BaseValue = target
                        a.CurrentValue = target
                        if it.onrep then set[it.onrep](set, { BaseValue = oldBase or cur, CurrentValue = cur }) end
                    end)
                    if not ok and lockErrors < 20 then
                        lockErrors = lockErrors + 1
                        log("lock %s error: %s", it.key, tostring(err))
                    end
                end
            end
        end
    end
end

-- ---------- items (T5a) ----------
-- Survey 2026-11-04 (evidence/t5a): ItemDataStruct.MaterialType 1-7 are upgrade / snap-soul / progression materials.
-- Only those and money are editable; story items, treasures, pages, flowers and the rest are listed read-only.
local ITEM_MAX, MONEY_MAX = 9999, 99999999
local lastInventoryRequest, lastOpSeq, inventorySeq = nil, nil, 0
local opResults = {}

local function text(v)
    if v == nil then return nil end
    local s = safe(function() return v:ToString() end)
    if type(s) == "string" and s ~= "" then return s end
    return nil
end

local function itemEntry(id, info)
    local d = safe(function() return info.ItemDataStruct end)
    local material = safe(function() return d.MaterialType end)
    local name = safe(function() return text(d.ItemName.ValueCache) end) or safe(function() return text(d.ItemName.Key) end)
    return {
        id = id,
        name = name or NULL,
        amount = safe(function() return info.CurrentAmount end) or NULL,
        hasMax = safe(function() return info.HasMaxAmount end) or NULL,
        type = safe(function() return d.ItemType end) or NULL,
        detail = safe(function() return d.ItemDetailType end) or NULL,
        material = material or NULL,
        editable = type(material) == "number" and material >= 1 and material <= 7,
    }
end

-- Calls fn(idString, keyFName, info) for each inventory entry; stops when fn returns true.
local function eachItem(bp, fn)
    bp.Inventories:ForEach(function(key, value)
        local k = key:get()
        local id = safe(function() return k:ToString() end)
        if id then return fn(id, k, value:get()) end
    end)
end

local function playerPawn()
    local pc = UEHelpers.GetPlayerController()
    local pawn = isValid(pc) and safe(function() return pc.Pawn end) or nil
    if isValid(pawn) then return pawn end
    return nil
end

local function money(pawn) return safe(function() return pawn.ConsciousnessDust end) end

local function writeInventory(reason)
    local pawn = playerPawn()
    local bp = safe(function() return FindFirstOf("PBZBackPackSubsystem") end)
    local items = {}
    if isValid(bp) then
        local ok, err = pcall(eachItem, bp, function(id, _, info) table.insert(items, itemEntry(id, info)) end)
        if not ok then log("inventory read error: %s", tostring(err)) end
    end
    inventorySeq = inventorySeq + 1
    local record = {
        seq = inventorySeq, sessionId = sessionId, reason = reason, wallTime = os.time(),
        valid = pawn ~= nil and isValid(bp), money = pawn and money(pawn) or NULL,
        items = items, results = opResults,
    }
    local tmp = INVENTORY_FILE .. ".tmp"
    local f = io.open(tmp, "wb")
    if not f then log("cannot write %s", tmp) return end
    f:write(encode(record))
    f:close()
    os.remove(INVENTORY_FILE)
    local ok, err = os.rename(tmp, INVENTORY_FILE)
    if not ok then log("inventory rename error: %s", tostring(err)) end
end

local function applyOp(op)
    local result = { seq = op.seq, kind = op.kind, id = op.id, target = op.target, ok = false }
    local pawn = playerPawn()
    if not pawn then result.error = "no player" return result end
    if op.kind == "money" then
        local target = math.max(0, math.min(MONEY_MAX, math.floor(op.target)))
        local before = money(pawn)
        if type(before) ~= "number" then result.error = "money unreadable" return result end
        local diff = target - before
        if diff ~= 0 then
            local ok, err = pcall(function() pawn:ChangeDustNum(diff > 0, math.abs(diff)) end)
            if not ok then result.error = tostring(err) end
        end
        result.before, result.after = before, money(pawn)
        result.ok = result.after == target
        return result
    end
    local bp = safe(function() return FindFirstOf("PBZBackPackSubsystem") end)
    if not isValid(bp) then result.error = "no backpack" return result end
    local found
    pcall(eachItem, bp, function(id, key, info)
        if id == op.id then found = { key = key, info = info } return true end
    end)
    if not found then result.error = "unknown item" return result end
    local entry = itemEntry(op.id, found.info)
    if not entry.editable then result.error = "not editable" return result end
    local target = math.max(0, math.min(ITEM_MAX, math.floor(op.target)))
    local before = entry.amount
    if type(before) ~= "number" then result.error = "amount unreadable" return result end
    local diff = target - before
    if diff ~= 0 then
        local ok, err = pcall(function()
            if diff > 0 then bp:AddPropsToBackPack(diff, found.key, false)
            else bp:RemovePropsFromBackPack(-diff, found.key, true) end
        end)
        if not ok then result.error = tostring(err) end
    end
    result.before = before
    result.after = safe(function() return found.info.CurrentAmount end)
    result.ok = result.after == target
    return result
end

local function itemsTick()
    -- The first config seen only sets the baselines: requests and edits made before this game started are not replayed.
    if lastInventoryRequest == nil then
        lastInventoryRequest = config.inventoryRequest
        local top = 0
        for _, op in ipairs(config.itemOps) do top = math.max(top, op.seq) end
        lastOpSeq = top
        return
    end
    local changed = false
    for _, op in ipairs(config.itemOps) do
        if op.seq > lastOpSeq then
            lastOpSeq = op.seq
            local result
            if not config.compatible then
                result = { seq = op.seq, kind = op.kind, id = op.id, target = op.target, ok = false, error = "incompatible game build" }
            else
                local ok, r = pcall(applyOp, op)
                result = ok and r or { seq = op.seq, ok = false, error = tostring(r) }
            end
            log("item op %d %s %s -> %s: %s %s", op.seq, tostring(op.kind), tostring(op.id), tostring(op.target),
                result.ok and "ok" or "failed", tostring(result.error or ""))
            table.insert(opResults, result)
            while #opResults > 50 do table.remove(opResults, 1) end
            changed = true
        end
    end
    if changed then
        writeInventory("edit")
    elseif config.inventoryRequest ~= lastInventoryRequest then
        lastInventoryRequest = config.inventoryRequest
        writeInventory("request")
    end
end

-- ---------- auto deflect / dodge (T5b) ----------
-- Probe 2026-11-04 (evidence/t5b): frames appear about 0.28 s before the hit (IsDodge ones about 0.48 s); a frame
-- whose HitGameTime is not a real time (projectiles) is answered as soon as it appears.
local PARRY_TICK_MS = 16
local MIN_PRESS_GAP = 0.08 -- seconds between two presses
local statics
local parryCmdSeq = 0

local function parryWanted() return config.compatible and config.modules.parry end

local function gameTime(world)
    if not isValid(statics) then statics = UEHelpers.GetGameplayStatics() end
    return safe(function() return statics:GetTimeSeconds(world) end)
end

-- One line "PBZPARRY <session> <seq> <deflect|dodge> end"; the manager presses the key once per new seq.
-- The trailing "end" lets the manager ignore a half-written file.
local function press(kind)
    return pcall(function()
        parryCmdSeq = parryCmdSeq + 1
        local f = assert(io.open(PARRY_CMD_FILE, "wb"))
        f:write(string.format("PBZPARRY %s %d %s end\n", sessionId, parryCmdSeq, kind))
        f:close()
    end)
end

local function parryTick()
    parryActive = false
    if not parryWanted() then
        parry.pending, parry.handled = {}, {}
        return
    end
    local pc = UEHelpers.GetPlayerController()
    local pawn = isValid(pc) and safe(function() return pc.Pawn end) or nil
    if not isValid(pawn) then parry.pawnAddr = nil return end
    local addr = safe(function() return pawn:GetAddress() end)
    if addr ~= parry.pawnAddr or not isValid(parry.comp) then
        parry.pawnAddr = addr
        parry.comp = safe(function() return pawn["Deflect Component"] end)
        parry.handled, parry.pending = {}, {}
    end
    if not isValid(parry.comp) then return end
    parryActive = true
    local world = safe(function() return pawn:GetWorld() end)
    local now = gameTime(world)
    if type(now) ~= "number" then return end
    local lead = config.parry.leadMs / 1000

    -- Outcome of earlier presses: health lower shortly after the expected hit means the hit landed.
    local hp = safe(function() return pawn.AttributeSetBase.Health.CurrentValue end)
    for i = #parry.pending, 1, -1 do
        local p = parry.pending[i]
        if now >= p.checkAt then
            if type(hp) == "number" and type(p.hp) == "number" and hp < p.hp - 0.5 then
                parry.hitsTaken = parry.hitsTaken + 1
                log("parry %s h=%d: hit taken (%.0f -> %.0f)", p.kind, p.handle, p.hp, hp)
            else
                parry.cleanHits = parry.cleanHits + 1
            end
            table.remove(parry.pending, i)
        end
    end

    local live = {}
    local due = {}
    safe(function()
        parry.comp.ReceiveFrames:ForEach(function(key, value)
            local h = key:get()
            live[h] = true
            if parry.handled[h] then return end
            local f = value:get()
            local hit = safe(function() return f.HitGameTime end)
            local isDodge = safe(function() return f.IsDodge end) == true
            local unknownTime = type(hit) ~= "number" or hit < now - 5 or hit > now + 5
            if unknownTime or hit - now <= lead then
                table.insert(due, { handle = h, dodge = isDodge, hit = unknownTime and now or hit })
            end
        end)
    end)
    for h in pairs(parry.handled) do if not live[h] then parry.handled[h] = nil end end

    table.sort(due, function(a, b) return a.hit < b.hit end)
    for _, d in ipairs(due) do
        local wantDodge = d.dodge
        if (wantDodge and not config.parry.dodge) or (not wantDodge and not config.parry.deflect) then
            parry.handled[d.handle] = true
        elseif now - parry.lastPress >= MIN_PRESS_GAP then
            local ok, err = press(wantDodge and "dodge" or "deflect")
            parry.handled[d.handle] = true
            parry.lastPress = now
            if ok then
                if wantDodge then parry.dodges = parry.dodges + 1 else parry.deflects = parry.deflects + 1 end
                table.insert(parry.pending, { handle = d.handle, kind = wantDodge and "dodge" or "deflect", hp = hp,
                    checkAt = math.max(d.hit, now) + 0.25 })
            else
                log("parry press error: %s", tostring(err))
            end
        end
    end
end

-- ---------- loop ----------
local sinceConfig = CONFIG_POLL_MS
LoopInGameThreadWithDelay(SAMPLE_MS, function()
    sinceConfig = sinceConfig + SAMPLE_MS
    if sinceConfig >= CONFIG_POLL_MS then
        sinceConfig = 0
        local ok, err = pcall(pollConfig)
        if not ok then log("config error: %s", tostring(err)) end
    end
    local lok, lerr = pcall(lockTick)
    if not lok and lockErrors < 20 then
        lockErrors = lockErrors + 1
        log("lock error: %s", tostring(lerr))
    end
    lockActive = lockWanted() == true
    if config.revision > 0 then
        local iok, ierr = pcall(itemsTick)
        if not iok then log("items error: %s", tostring(ierr)) end
    end
    local ok, record = pcall(sample)
    if not ok then
        record = { schemaVersion = SCHEMA_VERSION, sessionId = sessionId, sequence = sequence,
            monotonicMs = math.floor(os.clock() * 1000), lifecycle = "unknown", errors = { tostring(record) } }
    end
    local wok, werr = pcall(writeLine, record)
    if not wok then log("write error: %s", tostring(werr)) end
end)

-- Separate fast loop: attack frames live only about 0.3 s, so 200 ms sampling is too coarse.
LoopInGameThreadWithDelay(PARRY_TICK_MS, function()
    local ok, err = pcall(parryTick)
    if not ok then log("parry error: %s", tostring(err)) end
end)

log("v%s loaded, session %s", SUITE_VERSION, sessionId)
