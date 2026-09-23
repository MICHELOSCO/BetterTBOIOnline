-- The Future (v1.8) draws its lightning timer from TheFuture.rng inside MC_POST_RENDER
-- (tf_scripts/thefuture.lua:226). Render runs a different number of times on each PC, so
-- that shared RNG drifts apart and enemy AI using it (e.g. Selfish Knight turning) desyncs.
-- Fix: any TheFuture:RandomInt call without its own RNG, made during render, uses a
-- separate cosmetic RNG instead. Gameplay calls (MC_POST_UPDATE/NPC_UPDATE) are untouched.
local mod = RegisterMod("The Future Online Fix", 1)
local inRender = false
local cosmetic = RNG()
cosmetic:SetSeed(1, 35)

mod:AddPriorityCallback(ModCallbacks.MC_POST_RENDER, CallbackPriority.IMPORTANT, function()
	inRender = true
end)
-- ponytail: assumes no other mod's MC_POST_RENDER runs after LATE+1 and calls TheFuture:RandomInt.
mod:AddPriorityCallback(ModCallbacks.MC_POST_RENDER, CallbackPriority.LATE + 1, function()
	inRender = false
end)

local wrapped = false
local function wrap()
	if wrapped or not TheFuture or not TheFuture.RandomInt then return end
	local original = TheFuture.RandomInt
	TheFuture.RandomInt = function(self, min, max, entrng)
		if inRender and not entrng then entrng = cosmetic end
		return original(self, min, max, entrng)
	end
	wrapped = true
	Isaac.DebugString("[BTO-FUTUREFIX] RandomInt wrapped")
end

wrap() -- works if The Future loaded before us; otherwise the first game start does it.
mod:AddCallback(ModCallbacks.MC_POST_GAME_STARTED, wrap)
