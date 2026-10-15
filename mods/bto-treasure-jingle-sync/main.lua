-- BTO Treasure Jingle Sync v1.0 (BetterTBOIOnline). Online, mods cannot make the treasure-room jingle draw from the synced RNG. Base game.
-- Every player in the party must have the same BTO mods at the same version (handshake at the bottom).
local NAME, VERSION = "bto-treasure-jingle-sync", "1.0"
local mod = RegisterMod("BTO Treasure Jingle Sync", 1)
local game = Game()
local function log(s) Isaac.DebugString("[" .. NAME .. "] " .. s) end

-- 08/10 desync 49365 (Xandred): in a treasure room his client alone ran Room:PlayMusic() from Lua (rng
-- history: 7eb4e7 <- 8b33ca, the luabridge void-method trampoline). Caller: "The Specialist for Good Items"
-- (workshop 2575911103), installed on his PC only: it restores the room music with Room:PlayMusic().
-- Natively (VA 0x7EB1B0) that method, for ROOM_TREASURE on the first visit, picks one of the four entry
-- jingles (ids 87-90) with the SYNCED global RNG: one extra draw on one client only. The native call on
-- room entry is not affected (it does not go through Lua). Same patching pattern as
-- resources/scripts/main.lua (BeginClass/EndClass): wrap __index of the class. Online, exactly in the
-- RNG-drawing case (treasure room, first visit) the Lua call is logged and skipped; elsewhere unchanged.
local ok, cls = pcall(function() return getmetatable(Room).__class end)
if ok and cls and cls.__index then
	local oldIndex = cls.__index
	local function playMusic(room)
		local first = room:IsFirstVisit()
		local skip = game:GetNumPlayers() >= 2 and room:GetType() == RoomType.ROOM_TREASURE and first
		log(string.format("Room:PlayMusic from Lua f=%d type=%d first=%s skip=%s",
			game:GetFrameCount(), room:GetType(), tostring(first), tostring(skip)))
		if skip then return end
		return oldIndex(room, "PlayMusic")(room)
	end
	rawset(cls, "__index", function(self, k)
		if k == "PlayMusic" then return playMusic end
		return oldIndex(self, k)
	end)
	log("loaded: Room:PlayMusic wrapped")
else
	log("Room metatable not reachable, fix off")
end

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
