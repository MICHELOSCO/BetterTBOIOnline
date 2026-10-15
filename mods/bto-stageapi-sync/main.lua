-- BTO StageAPI Sync v1.0 (BetterTBOIOnline). Keeps StageAPI custom stages (e.g. The Future) deterministic online. Needs StageAPI.
-- Every player in the party must have the same BTO mods at the same version (handshake at the bottom).
local NAME, VERSION = "bto-stageapi-sync", "1.0"
local mod = RegisterMod("BTO StageAPI Sync", 1)
local game = Game()
local function log(s) Isaac.DebugString("[" .. NAME .. "] " .. s) end

-- 1. StageAPI.RandomRNG is seeded once from Random() at mod load and never re-seeded: its state depends
--    on each PC's history. StageAPI uses it for gameplay: the random entrance door when the layout lacks
--    the door you came from (callbacks.lua:1350, moves ALL players: desyncs 23/09 17:18 and 01/10 42395),
--    devil-deal price (:1557), custom door particles. Re-seed it deterministically on each level/room.
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

-- 2. StageAPI's animated stage transition (custom trapdoor out of The Future calls
--    GotoCustomStage(stage, true), transitions.lua:959) issues the `stage` console command from
--    MC_GET_SHADER_PARAMS when a render-frame counter passes 150 (transitions.lua:8-38): the floor
--    changes at a game frame that depends on fps. With 2+ players: no animation, immediate command.
local wrappedGoto = false
local function wrapGoto()
	if wrappedGoto or not (StageAPI and StageAPI.GotoCustomStage) then return end
	local goto_ = StageAPI.GotoCustomStage
	StageAPI.GotoCustomStage = function(stage, playTransition, noForgetSeed)
		if playTransition and game:GetNumPlayers() >= 2 then
			playTransition = false
			log("stage transition animation skipped (multiplayer)")
		end
		return goto_(stage, playTransition, noForgetSeed)
	end
	wrappedGoto = true
	log("GotoCustomStage wrapped")
end
wrapGoto() -- the order in which mods load is not fixed: retry at run start
mod:AddPriorityCallback(ModCallbacks.MC_POST_GAME_STARTED, CallbackPriority.IMPORTANT - 1, wrapGoto)

-- 3. StageAPI preloads room data with ~100 console `goto` commands and then restarts the run, but only
--    on a client whose Lua state is fresh (first run since the game opened): online that client alone
--    diverges at frame 0. In any multi-player run start, EVERY client redoes the preload and the
--    one-sided restart is suppressed. (Verified 05/10 and 06/10: clients agree at frame 1.)
-- ponytail: "multi-player" = GetNumPlayers() > 1 (vanilla Lua has no online flag); local co-op too.
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
log("loaded")

-- BTO handshake begin (identical in every BTO mod: build-zip.sh checks it) ------------------------------
-- Online, at run start, the synced RNG is advanced a number of times derived from the set of active BTO
-- mods and their versions: a party with different BTO mods or versions fails at frame 1 ("Checksum
-- mismatch") instead of splitting mid-run (08/10 00:28: one player on an old version lost a 20-min run).
BTO_MODS = BTO_MODS or {}
BTO_MODS[NAME] = VERSION
if not BTO_HandshakeDraws then
	function BTO_HandshakeDraws()
		local names, h = {}, 7
		for k in pairs(BTO_MODS) do names[#names + 1] = k end
		table.sort(names)
		for _, k in ipairs(names) do
			local s = k .. "=" .. BTO_MODS[k] .. ";"
			for i = 1, #s do h = (h * 31 + s:byte(i)) % 1000003 end
		end
		return h % 1000 + 1, table.concat(names, ",")
	end
	mod:AddPriorityCallback(ModCallbacks.MC_POST_GAME_STARTED, CallbackPriority.LATE + 2, function()
		if Game():GetNumPlayers() < 2 then return end
		local n, names = BTO_HandshakeDraws()
		for _ = 1, n do Random() end
		Isaac.DebugString("[BTO] handshake draws=" .. n .. " mods=" .. names)
	end)
end
-- BTO handshake end --------------------------------------------------------------------------------------
