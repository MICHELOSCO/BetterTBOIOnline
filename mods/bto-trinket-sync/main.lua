-- BTO Trinket Sync v1.0 (BetterTBOIOnline). Online, trinket pickups are done by this mod, identically on every PC. Base game.
-- Every player in the party must have the same BTO mods at the same version (handshake at the bottom).
local NAME, VERSION = "bto-trinket-sync", "1.0"
local mod = RegisterMod("BTO Trinket Sync", 1)
local game = Game()
local function log(s) Isaac.DebugString("[" .. NAME .. "] " .. s) end

-- Every RNG-identical desync in the logs (19/09, 20/09, 24/09, 07/10 22:53) came the frame after a player
-- holding a trinket walked over another one (old trinket dropped, new one queued above the head); only the
-- swapping player's own velocity/tears differ, by <1%, on the other clients. The native path applies the
-- queued trinket's stats at a different frame on the owner's client than on the others. With 2+ players the
-- native pickup is bypassed and the whole swap is done here, on the collision frame, identically on every
-- client: old trinket removed and dropped at the player's feet, new one added immediately (hold-up
-- animation kept for looks). Gameplay unchanged: walking over a trinket still takes it.
-- ponytail: shop trinkets stay native (payment); with two full slots the first slot is the one dropped.
local sfx = SFXManager()
mod:AddCallback(ModCallbacks.MC_PRE_PICKUP_COLLISION, function(_, pickup, collider)
	local p = collider:ToPlayer()
	if not p or game:GetNumPlayers() < 2 or pickup:IsShopItem() then return end
	local d, pd, frame = pickup:GetData(), p:GetData(), game:GetFrameCount()
	-- cannot collect now: the native code only pushes the pickup, same on every client, leave it to it
	if p:IsCoopGhost() or not p:IsItemQueueEmpty() or p:IsHoldingItem() then return end
	if d.btoTaken or (pd.btoTrinketCooldown or 0) > frame then return true end
	local free
	for i = 0, p:GetMaxTrinkets() - 1 do
		if p:GetTrinket(i) == 0 then free = i break end
	end
	if not free then
		local old = p:GetTrinket(0)
		if not p:TryRemoveTrinket(old) then return true end
		Isaac.Spawn(EntityType.ENTITY_PICKUP, PickupVariant.PICKUP_TRINKET, old, p.Position, Vector.Zero, p):ToPickup().Touched = true
	end
	d.btoTaken = true
	pd.btoTrinketCooldown = frame + 30 -- like the vanilla wait before a dropped trinket can be taken again
	local id = pickup.SubType
	p:AddTrinket(id, not pickup.Touched)
	p:AnimateTrinket(id)
	sfx:Play(SoundEffect.SOUND_POWERUP1)
	local cfg = Isaac.GetItemConfig():GetTrinket(id & ~TrinketType.TRINKET_GOLDEN_FLAG)
	if cfg then game:GetHUD():ShowItemText(p, cfg) end -- the (player, item) overload localises the name itself
	pickup:Remove()
	return true
end, PickupVariant.PICKUP_TRINKET)
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
