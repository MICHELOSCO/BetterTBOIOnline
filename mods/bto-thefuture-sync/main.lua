-- BTO The Future Sync v1.0 (BetterTBOIOnline). Desync fixes specific to The Future's enemies. Needs The Future.
-- Every player in the party must have the same BTO mods at the same version (handshake at the bottom).
local NAME, VERSION = "bto-thefuture-sync", "1.0"
local mod = RegisterMod("BTO The Future Sync", 1)
local game = Game()
local function log(s) Isaac.DebugString("[" .. NAME .. "] " .. s) end

-- FireProjectiles mode 9 (N projectiles in a circle) starts the ring at ProjectileParams.CircleAngle,
-- "random by default": the constructor draws it, and rubber.lua:5 builds its ProjectileParams once at mod
-- load, when each PC's RNG is in its own state, so Rubber's death ring (rubber.lua:99) was rotated by a
-- per-PC constant (desync 07/10 16:26: 5 projectiles 6 degrees apart, global RNG identical on both PCs).
-- Pin it from the enemy's seed at fire time. Nevermore sets its own (nevermore.lua:239); other modes ignore it.
local wrapped = false
local function wrap()
	if wrapped or not (TheFuture and TheFuture.FireProjectiles) then return end
	local fire = TheFuture.FireProjectiles
	TheFuture.FireProjectiles = function(self, npc, pos, vec, mode, params)
		if mode == 9 and params then params.CircleAngle = npc.InitSeed % 360 end
		return fire(self, npc, pos, vec, mode, params)
	end
	wrapped = true
	log("FireProjectiles circle angle pinned")
end
wrap() -- the order in which mods load is not fixed: retry at run start
mod:AddPriorityCallback(ModCallbacks.MC_POST_GAME_STARTED, CallbackPriority.IMPORTANT, wrap)
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
