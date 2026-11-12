-- One-shot unlock (F8 once, in 戏梦山庄 near the painting). Lines start with "UNLOCK".
-- Plays the painting's own dissolve, then turns off its collision so the passage behind it can be entered.
-- Only changes the live scene; nothing is written to the save file by this script.
local P = PBZProbe
local PATH = "/Game/Maps/B08_09_12_13/PBZ_Ximeng_WP.PBZ_Ximeng_WP:PersistentLevel.BP_Guahua_Huanshu_C_UAID_BCFCE7058A5725FB02_1632900079"

local painting = P.safe(function() return StaticFindObject(PATH) end)
if not P.isValid(painting) then P.log("UNLOCK painting not found (not in 戏梦山庄 or not loaded)") return end

local ok = P.safe(function() painting["Begain Dissolve"](painting) return true end)
P.log("UNLOCK dissolve called: %s", tostring(ok))

ExecuteWithDelay(3000, function()
    ExecuteInGameThread(function()
        if not P.isValid(painting) then P.log("UNLOCK painting gone") return end
        P.safe(function() painting:SetActorEnableCollision(false) end)
        P.safe(function() painting.StaticMesh:SetCollisionEnabled(0) end)
        P.log("UNLOCK collision off: actor=%s mesh=%s visible=%s",
            tostring(P.safe(function() return painting:GetActorEnableCollision() end)),
            tostring(P.safe(function() return painting.StaticMesh:GetCollisionEnabled() end)),
            tostring(P.safe(function() return painting.StaticMesh:IsVisible() end)))
    end)
end)
