-- The Future (v1.8) draws its lightning timer from TheFuture.rng inside MC_POST_RENDER
-- (tf_scripts/thefuture.lua:226). Render runs a different number of times on each PC, so
-- that shared RNG drifts apart and enemy AI using it (e.g. Selfish Knight turning) desyncs.
-- Fix: any TheFuture:RandomInt call without its own RNG, made during render, uses a
-- separate cosmetic RNG instead. Gameplay calls (MC_POST_UPDATE/NPC_UPDATE) are untouched.
local mod = RegisterMod("The Future Online Fix", 1)
local game = Game()
local renderDepth = 0
local cosmetic = RNG()
cosmetic:SetSeed(1, 35)

-- Bracket EVERY render callback (world render + per-entity renders): while any of them runs,
-- renderDepth > 0. Render runs a different number of times on each PC, so any synced random
-- source consumed inside it drifts apart between clients.
-- ponytail: assumes no other mod's render callback runs after LATE+1 and draws random numbers.
for _, id in ipairs({
	ModCallbacks.MC_POST_RENDER, ModCallbacks.MC_POST_FAMILIAR_RENDER, ModCallbacks.MC_POST_NPC_RENDER,
	ModCallbacks.MC_POST_PLAYER_RENDER, ModCallbacks.MC_POST_PICKUP_RENDER, ModCallbacks.MC_POST_TEAR_RENDER,
	ModCallbacks.MC_POST_PROJECTILE_RENDER, ModCallbacks.MC_POST_LASER_RENDER, ModCallbacks.MC_POST_KNIFE_RENDER,
	ModCallbacks.MC_POST_EFFECT_RENDER, ModCallbacks.MC_POST_BOMB_RENDER,
}) do
	mod:AddPriorityCallback(id, CallbackPriority.IMPORTANT, function() renderDepth = renderDepth + 1 end)
	mod:AddPriorityCallback(id, CallbackPriority.LATE + 1, function() renderDepth = math.max(renderDepth - 1, 0) end)
end

-- GENERIC GUARD (any mod): during render, the engine-synced draws Random() / RandomVector() come from a
-- local cosmetic RNG instead. Outside render, math.random is re-seeded from (run seed, frame), so its
-- stream no longer depends on how many renders ran. Update-time behaviour is otherwise unchanged.
-- ponytail: mods that cached `local Random = Random` before this mod loaded are not covered.
local origRandom, origRandomVector, origMathRandom = Random, RandomVector, math.random
Random = function() if renderDepth > 0 then return cosmetic:Next() end return origRandom() end
RandomVector = function()
	if renderDepth > 0 then return Vector.FromAngle(cosmetic:RandomFloat() * 360) end
	return origRandomVector()
end
local lastFrame = -1
math.random = function(...)
	if renderDepth == 0 then
		-- pcall: can be called while mods load, before a run (and its seeds) exists.
		local ok, f = pcall(game.GetFrameCount, game)
		if ok and f ~= lastFrame then
			lastFrame = f
			pcall(function() math.randomseed((game:GetSeeds():GetStartSeed() + f * 7919) % 2147483647) end)
		end
	end
	return origMathRandom(...)
end

local wrapped = false
local function wrap()
	if wrapped or not TheFuture or not TheFuture.RandomInt then return end
	local original = TheFuture.RandomInt
	TheFuture.RandomInt = function(self, min, max, entrng)
		if renderDepth > 0 and not entrng then entrng = cosmetic end
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

Isaac.DebugString("[BTO-FUTUREFIX] sync guard active (render-time Random/RandomVector isolated, math.random per-frame seeded)")
