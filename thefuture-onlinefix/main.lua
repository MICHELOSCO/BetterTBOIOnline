-- The Future Online Fix v0.6 — keeps StageAPI / The Future deterministic across online clients.
-- Loads last (folder name sorts after the other mods), so the global overrides below see every mod.
-- Each block is independent; "ponytail:" notes mark known ceilings. See docs/desync-audit.md.
local mod = RegisterMod("The Future Online Fix", 1)
local game = Game()
local cosmetic = RNG() -- local-only RNG for anything drawn while rendering
cosmetic:SetSeed(1, 35)

local function log(s) Isaac.DebugString("[BTO-FUTUREFIX] " .. s) end

-- ---------------------------------------------------------------------------------------------
-- 1. Render-phase detection. Render runs a different number of times on each PC, so any synced
--    random source consumed inside a render callback drifts the clients apart.
--    The vanilla dispatcher (_RunCallback in resources/scripts/main.lua) stops at the first handler
--    that errors or returns non-nil, so an "end of render" handler can be skipped (Fair Co-op Items
--    errors in MC_POST_RENDER every session). Hence no counting: every render callback sets the flag,
--    every update-phase callback clears it. First handler to run in each dispatch (IMPORTANT).
-- ponytail: a mod spawning entities *during render* would clear the flag mid-render (none installed does).
local inRender = false
local notUpdate = { MC_INPUT_ACTION = true, MC_EXECUTE_CMD = true, MC_EVALUATE_CACHE = true, MC_PRE_MOD_UNLOAD = true }
for name, id in pairs(ModCallbacks) do
	if name:find("RENDER") or name == "MC_GET_SHADER_PARAMS" then
		mod:AddPriorityCallback(id, CallbackPriority.IMPORTANT, function() inRender = true end)
	elseif not notUpdate[name] then
		mod:AddPriorityCallback(id, CallbackPriority.IMPORTANT, function() inRender = false end)
	end
end

-- ---------------------------------------------------------------------------------------------
-- 2. Random sources used while rendering go to the cosmetic RNG; update-time behaviour unchanged.
local origRandom, origRandomVector, origMathRandom = Random, RandomVector, math.random
Random = function() if inRender then return cosmetic:Next() end return origRandom() end
RandomVector = function()
	if inRender then return Vector.FromAngle(cosmetic:RandomFloat() * 360) end
	return origRandomVector()
end
-- math.random is a per-process C stream (never synced). Outside render it is re-seeded from
-- (run seed, game frame) on each new frame, so its values depend only on the synced simulation.
local lastFrame = -1
math.random = function(...)
	if not inRender then
		local ok, f = pcall(game.GetFrameCount, game) -- can be called while mods load, before a run exists
		if ok and f ~= lastFrame then
			lastFrame = f
			pcall(function() math.randomseed((game:GetSeeds():GetStartSeed() + f * 7919) % 2147483647) end)
		end
	end
	return origMathRandom(...)
end

if StageAPI then
	-- StageAPI.Random(a, b) without an explicit rng uses StageAPI.RandomRNG (see 3.); in render → cosmetic.
	local sr, srf = StageAPI.Random, StageAPI.RandomFloat
	StageAPI.Random = function(a, b, rng) if inRender and not rng then rng = cosmetic end return sr(a, b, rng) end
	StageAPI.RandomFloat = function(a, b, rng) if inRender and not rng then rng = cosmetic end return srf(a, b, rng) end
end

-- The Future (v1.8) draws its lightning timer from TheFuture.rng inside MC_POST_RENDER
-- (tf_scripts/thefuture.lua:226); enemy AI (Selfish Knight turning, …) uses the same RNG in update.
-- FireProjectiles mode 9 (N projectiles in a circle) starts the ring at ProjectileParams.CircleAngle,
-- "random by default": the constructor draws it, and rubber.lua:5 builds its ProjectileParams once at
-- mod load, when each PC's RNG is in its own state, so Rubber's death ring (rubber.lua:99) was rotated
-- by a per-PC constant (desync 07/10 16:26: 5 projectiles 6° apart, global RNG identical on both PCs).
-- Pin it from the enemy's seed at fire time. Nevermore sets its own (nevermore.lua:239); other modes ignore it.
local wrapped = false
local function wrap()
	if wrapped or not TheFuture or not TheFuture.RandomInt then return end
	local original = TheFuture.RandomInt
	TheFuture.RandomInt = function(self, min, max, entrng)
		if inRender and not entrng then entrng = cosmetic end
		return original(self, min, max, entrng)
	end
	local fire = TheFuture.FireProjectiles
	if fire then
		TheFuture.FireProjectiles = function(self, npc, pos, vec, mode, params)
			if mode == 9 and params then params.CircleAngle = npc.InitSeed % 360 end
			return fire(self, npc, pos, vec, mode, params)
		end
	end
	wrapped = true
	log("RandomInt wrapped, FireProjectiles circle angle pinned=" .. tostring(fire ~= nil))
end
wrap()
mod:AddCallback(ModCallbacks.MC_POST_GAME_STARTED, wrap)

-- ---------------------------------------------------------------------------------------------
-- 3. StageAPI.RandomRNG is seeded once from Random() at mod load and never re-seeded: its state
--    depends on each PC's history. StageAPI uses it for gameplay: the random entrance door when the
--    layout lacks the door you came from (callbacks.lua:1350, moves ALL players: desyncs 23/09 17:18
--    and 01/10 42395 match this exactly), devil-deal price (:1557), custom door particles.
--    Re-seed it deterministically before any StageAPI handler runs on each level/room.
local function reseedStageRNG(seed)
	if not (StageAPI and StageAPI.RandomRNG) then return end
	if not seed or seed == 0 then seed = 1 end
	StageAPI.RandomRNG:SetSeed(seed, 0)
end
mod:AddPriorityCallback(ModCallbacks.MC_POST_NEW_LEVEL, CallbackPriority.IMPORTANT - 1, function()
	reseedStageRNG(game:GetSeeds():GetStageSeed(game:GetLevel():GetStage()))
end)
mod:AddPriorityCallback(ModCallbacks.MC_POST_NEW_ROOM, CallbackPriority.IMPORTANT - 1, function()
	reseedStageRNG(game:GetRoom():GetDecorationSeed())
end)

-- ---------------------------------------------------------------------------------------------
-- 4. StageAPI's animated stage transition (custom trapdoor out of The Future calls
--    GotoCustomStage(stage, true), transitions.lua:959) issues the `stage` console command from
--    MC_GET_SHADER_PARAMS when a render-frame counter passes 150 (transitions.lua:8-38): the floor
--    changes at a game frame that depends on fps. With 2+ players: no animation, immediate command.
if StageAPI and StageAPI.GotoCustomStage then
	local goto_ = StageAPI.GotoCustomStage
	StageAPI.GotoCustomStage = function(stage, playTransition, noForgetSeed)
		if playTransition and game:GetNumPlayers() >= 2 then
			playTransition = false
			log("stage transition animation skipped (multiplayer)")
		end
		return goto_(stage, playTransition, noForgetSeed)
	end
end

-- ---------------------------------------------------------------------------------------------
-- 5. StageAPI preloads room data with ~100 console `goto` commands and then restarts the run, but
--    only on a client whose Lua state is fresh (first run since the game opened): online that client
--    alone diverges at frame 0. In any multi-player run start, EVERY client redoes the preload and
--    the one-sided restart is suppressed. (Verified 05/10 and 06/10: clients agree at frame 1.)
-- ponytail: "multi-player" = GetNumPlayers() > 1 (vanilla Lua has no online flag); local co-op too.
local forced, inMirror, mirrorFrame = false, false, -1

mod:AddPriorityCallback(ModCallbacks.MC_POST_GAME_STARTED, CallbackPriority.IMPORTANT, function(_, continued)
	-- 6. below already ran every POST_GAME_STARTED handler for this frame: swallow a duplicate dispatch.
	if not inMirror and mirrorFrame == game:GetFrameCount() then return true end
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
	log(string.format("preload forced for %d shapes, players=%d", n, game:GetNumPlayers()))
end)

mod:AddPriorityCallback(ModCallbacks.MC_POST_GAME_STARTED, CallbackPriority.LATE + 1, function()
	if not forced then return end
	forced = false
	if StageAPI.DataLoadNeedsRestart then
		StageAPI.DataLoadNeedsRestart = false -- same run state on every client, no one-sided restart
		log("StageAPI restart suppressed")
	end
end)

-- ---------------------------------------------------------------------------------------------
-- 6. EXPERIMENTAL — join-in-progress. When someone joins, the game reloads the match state on every
--    client, but MC_POST_GAME_STARTED fires only on the joiner (host log 24/09: none at the join).
--    So only the joiner runs the mods' run-start code (StageAPI preload, TheFuture.rng re-seed…) and
--    the match desyncs ~50 frames later at the next Level::Init. Here the clients that were already in
--    the match detect the reload (all their players removed, player count grew, mid-run) and run the
--    same MC_POST_GAME_STARTED dispatch at the same point (right after the room load) as the joiner.
--    Joins are processed only during a vanilla floor transition, so the old floor's mod data is
--    discarded anyway. Revive reloads (count unchanged) and local co-op joins (no reload) do not match.
local lastPlayers, removed = 0, 0
mod:AddPriorityCallback(ModCallbacks.MC_POST_UPDATE, CallbackPriority.IMPORTANT, function()
	lastPlayers, removed = game:GetNumPlayers(), 0
end)
mod:AddCallback(ModCallbacks.MC_POST_ENTITY_REMOVE, function() removed = removed + 1 end, EntityType.ENTITY_PLAYER)
mod:AddCallback(ModCallbacks.MC_PRE_GAME_EXIT, function() lastPlayers, removed = 0, 0 end)
mod:AddCallback(ModCallbacks.MC_POST_GAME_END, function() lastPlayers, removed = 0, 0 end)

mod:AddPriorityCallback(ModCallbacks.MC_POST_NEW_ROOM, CallbackPriority.LATE + 1, function()
	local players, frame = game:GetNumPlayers(), game:GetFrameCount()
	if frame > 0 and lastPlayers >= 1 and players > lastPlayers and removed >= lastPlayers then
		lastPlayers, removed, mirrorFrame = players, 0, frame
		log(string.format("join mirror: running POST_GAME_STARTED for %d players at frame %d", players, frame))
		inMirror = true
		local ok, err = pcall(Isaac.RunCallback, ModCallbacks.MC_POST_GAME_STARTED, false)
		inMirror = false
		if not ok then log("join mirror error: " .. tostring(err)) end
	end
end)

log("v0.6 active: render guard, StageAPI RNG re-seed, no animated stage change online, preload equaliser, join mirror, circle-shot angle pinned")
