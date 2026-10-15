-- BTO Fair Co-op Crash Fix v1.0 (BetterTBOIOnline). Fair Co-op Items no longer crashes every client after a split. Needs Fair Co-op Items.
-- Every player in the party must have the same BTO mods at the same version (handshake at the bottom).
local NAME, VERSION = "bto-faircoop-crash-fix", "1.0"
local mod = RegisterMod("BTO Fair Co-op Crash Fix", 1)
local game = Game()
local function log(s) Isaac.DebugString("[" .. NAME .. "] " .. s) end

-- Fair Co-op Items keeps its own list of EntityPlayer references, rebuilt only on MC_POST_NEW_ROOM (its
-- main.lua:33/:154), and walks it on every render frame in treasure/angel/planetarium rooms (onUpdate,
-- :241-266). When a desync splits the lobby the game removes the other players at once and the next render
-- calls player:ToPlayer() on freed entities: native exception, "Caught exception, writing minidump", on
-- every client at the same moment (09/10 01:07, angel room, all three PCs). Its room check is a global
-- function: after any player removal it answers "no" until the next room load, where that mod rebuilds its
-- list. Its shop/boss paths touch no player references.
local stale, installed = false, false
local function install()
	if installed or type(isRightRoom) ~= "function" then return end
	local fairRightRoom = isRightRoom
	isRightRoom = function()
		if stale then return false end
		return fairRightRoom()
	end
	installed = true
	log("stale-player guard installed")
end
mod:AddCallback(ModCallbacks.MC_POST_ENTITY_REMOVE, function() stale = true end, EntityType.ENTITY_PLAYER)
mod:AddPriorityCallback(ModCallbacks.MC_POST_NEW_ROOM, CallbackPriority.LATE + 2, function() stale = false end)
install() -- the order in which mods load is not fixed: retry at run start
mod:AddPriorityCallback(ModCallbacks.MC_POST_GAME_STARTED, CallbackPriority.IMPORTANT, install)
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
