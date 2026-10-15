-- BTO Join Sync v1.0 (BetterTBOIOnline). EXPERIMENTAL: a player rejoining a run mid-way does not desync the party.
-- Every player in the party must have the same BTO mods at the same version (handshake at the bottom).
local NAME, VERSION = "bto-join-sync", "1.0"
local mod = RegisterMod("BTO Join Sync", 1)
local game = Game()
local function log(s) Isaac.DebugString("[" .. NAME .. "] " .. s) end

-- When someone joins, the game reloads the match state on every client, but MC_POST_GAME_STARTED fires only
-- on the joiner (host log 24/09: none at the join). So only the joiner runs the mods' run-start code
-- (StageAPI preload, TheFuture.rng re-seed...) and the match desyncs ~50 frames later at the next
-- Level::Init. Here the clients that were already in the match detect the reload (all their players
-- removed, player count grew, mid-run) and run the same MC_POST_GAME_STARTED dispatch at the same point
-- (right after the room load) as the joiner. Joins are processed only during a vanilla floor transition,
-- so the old floor's mod data is discarded anyway. Revive reloads (count unchanged) and local co-op joins
-- (no reload) do not match.
local lastPlayers, removed, inMirror, mirrorFrame = 0, 0, false, -1

-- The mirror already ran every MC_POST_GAME_STARTED handler for this frame: swallow a duplicate dispatch.
mod:AddPriorityCallback(ModCallbacks.MC_POST_GAME_STARTED, CallbackPriority.IMPORTANT - 50, function()
	if not inMirror and mirrorFrame == game:GetFrameCount() then return true end
end)
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
