-- Edited between samples; reload in game with F11. Read-only accessors only.
local function attr(ctx, name)
    return ctx.pawn.AttributeSetBase[name].CurrentValue
end

-- The backpack subsystem lives for the whole world; re-find it only when the cached one is gone.
local backpack
local function bp(ctx)
    if not ctx.P.isValid(backpack) then backpack = FindFirstOf("PBZBackPackSubsystem") end
    return backpack
end

local function medicine(ctx, slotProp, field)
    local b = bp(ctx)
    local id = b[slotProp]:ToString()
    local result
    b.MedicineInfos:ForEach(function(key, value)
        if key:get():ToString() == id then
            result = value:get()[field]
            return true
        end
    end)
    return result
end

return {
    { label = "hp", fn = function(c) return attr(c, "Health") end },
    { label = "hpMax", fn = function(c) return attr(c, "Health_Max") end },
    { label = "inCombat", fn = function(c) return c.P.tagCount(c.pawn, "CharacterState.InCombat") end },
    { label = "dead", fn = function(c) return c.P.tagCount(c.pawn, "CharacterState.Dead") end },
    { label = "topMed", fn = function(c) return bp(c).TopMedicineID:ToString() end },
    { label = "topCur", fn = function(c) return medicine(c, "TopMedicineID", "CurrentTime") end },
    { label = "topDisp", fn = function(c) return medicine(c, "TopMedicineID", "DisplayTime") end },
    { label = "botMed", fn = function(c) return bp(c).BottomMedicineID:ToString() end },
    { label = "botCur", fn = function(c) return medicine(c, "BottomMedicineID", "CurrentTime") end },
    { label = "botDisp", fn = function(c) return medicine(c, "BottomMedicineID", "DisplayTime") end },
    { label = "extra", fn = function(c) return bp(c).MedicineExtraUsage end },
    { label = "shachi", fn = function(c) return attr(c, "Shachi") end },
    { label = "shachiMax", fn = function(c) return attr(c, "Shachi_Max") end },
    { label = "ult", fn = function(c) return attr(c, "UltimateCharge") end },
    { label = "ultCount", fn = function(c) return attr(c, "UltimateChargeCount") end },
    { label = "spirit", fn = function(c) return attr(c, "SpiritShadow") end },
    { label = "spiritMax", fn = function(c) return attr(c, "SpiritShadow_Max") end },
}
