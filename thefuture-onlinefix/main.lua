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

-- EXPERIMENTAL (26/09/2026): StageAPI preloads room data with ~100 console `goto` commands and then
-- restarts the run, but only on a client whose Lua state is fresh (first run since the game opened).
-- Online that client alone diverges at frame 0. Here, in any multi-player run start, EVERY client is
-- forced to redo the preload (all clients identical) and the restart is suppressed.
-- ponytail: "multi-player" = GetNumPlayers() > 1 (vanilla Lua has no online flag); local couch co-op
-- would also trigger it. Solo runs are untouched. Revert by deleting everything below this line.
local forced = false

mod:AddPriorityCallback(ModCallbacks.MC_POST_GAME_STARTED, CallbackPriority.IMPORTANT, function(_, continued)
	if continued or not StageAPI or not StageAPI.PreloadedGotoData or StageAPI.InTestMode
		or game:GetNumPlayers() < 2 then return end
	local n = 0
	for _, shapes in pairs(StageAPI.PreloadedGotoData) do
		for shape in pairs(shapes) do
			shapes[shape] = false -- makes StageAPI treat it as not loaded yet
			n = n + 1
		end
	end
	forced = true
	Isaac.DebugString(string.format("[BTO-FUTUREFIX] preload forced for %d shapes, players=%d", n, game:GetNumPlayers()))
end)

mod:AddPriorityCallback(ModCallbacks.MC_POST_GAME_STARTED, CallbackPriority.LATE + 1, function()
	if not forced then return end
	forced = false
	if StageAPI.DataLoadNeedsRestart then
		StageAPI.DataLoadNeedsRestart = false -- same run state on every client, no one-sided restart
		Isaac.DebugString("[BTO-FUTUREFIX] StageAPI restart suppressed")
	end
end)
