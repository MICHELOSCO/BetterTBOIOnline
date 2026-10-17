-- BTO Sirens Fix v1.0 (BetterTBOIOnline). [Fish] Siren's Items Over Mom's loads without a Lua error. Needs that mod.
-- Every player in the party must have the same BTO mods at the same version (handshake at the bottom).
local NAME, VERSION = "bto-sirens-fix", "1.0"
local mod = RegisterMod("BTO Sirens Fix", 1)
local function log(s) Isaac.DebugString("[" .. NAME .. "] " .. s) end

-- Its main.lua (Workshop 3650363740) ends with mod:AddCallback(MC_POST_PLAYER_UPDATE, mod.onUpdate), and
-- mod.onUpdate does not exist: "bad argument #3 to 'AddCallback' (function expected, got nil)" at every
-- launch. Harmless (it is the last line), but any Lua error makes BTO Sync send the game's log after each
-- session. Mods load in the order of their metadata name, so "BTO ..." always runs before "[fish] ...":
-- that one registration is skipped. Every other mod still gets the error.
local SIREN = "Siren's Complete Set"
local isaacAdd = Isaac.AddCallback
rawset(Isaac, "AddCallback", function(m, id, fn, param)
	if fn == nil and type(m) == "table" and m.Name == SIREN then
		log("skipped " .. SIREN .. "'s callback with no function")
		return
	end
	return isaacAdd(m, id, fn, param)
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
