-- Boss probe, read-only (F8 once while a boss health bar is on screen). Lines start with "BOSS". No loops.
-- Lists every PBZHUDLayout and its boss map, every WB_BossHPBar widget with its character, visibility and health,
-- and the player's lock-on / nearby characters flagged bIsBoss.
local P = PBZProbe

local function str(v)
    if v == nil then return "nil" end
    if type(v) == "userdata" then
        local s = P.safe(function() return v:ToString() end)
        if s then return s end
    end
    return tostring(v)
end

local function hp(actor)
    local c = P.safe(function() return actor.AttributeSetBase.Health.CurrentValue end)
    local m = P.safe(function() return actor.AttributeSetBase.Health_Max.CurrentValue end)
    return string.format("%s/%s", str(c), str(m))
end

local layouts = FindAllOf("PBZHUDLayout") or {}
P.log("BOSS layouts: %d", #layouts)
for i, l in ipairs(layouts) do
    local n = 0
    local keys = {}
    P.safe(function()
        l.RegisterBossHandles:ForEach(function(k)
            n = n + 1
            local a = P.safe(function() return k:get():Get() end)
            table.insert(keys, P.isValid(a) and (P.fullName(a) .. " hp=" .. hp(a)) or "invalid")
        end)
    end)
    P.log("BOSS layout[%d] %s visible=%s map=%d %s", i, P.fullName(l), str(P.safe(function() return l:IsVisible() end)), n, table.concat(keys, " | "))
end

local bars = FindAllOf("WB_BossHPBar_C") or {}
P.log("BOSS hp bar widgets: %d", #bars)
for i, w in ipairs(bars) do
    local ch = P.safe(function() return w.Character end)
    P.log("BOSS bar[%d] %s visible=%s inViewport=%s name=%s char=%s hp=%s", i, P.fullName(w),
        str(P.safe(function() return w:IsVisible() end)), str(P.safe(function() return w:IsInViewport() end)),
        str(P.safe(function() return w.BossName:GetText():ToString() end)),
        P.isValid(ch) and P.fullName(ch) or "nil", P.isValid(ch) and hp(ch) or "-")
end

local chars = FindAllOf("ProjectPBZCharacter") or {}
local bosses = 0
for _, c in ipairs(chars) do
    if P.safe(function() return c.bIsBoss end) == true then
        bosses = bosses + 1
        P.log("BOSS flagged character %s hp=%s important=%s", P.fullName(c), hp(c), str(P.safe(function() return c.bImportantEnemy end)))
    end
end
P.log("BOSS characters: %d total, %d with bIsBoss", #chars, bosses)
