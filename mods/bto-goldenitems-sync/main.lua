-- BTO Golden Items Sync v1.0 (BetterTBOIOnline). Golden pedestals are the same on every client. Needs Golden Items.
-- Every player in the party must have the same BTO mods at the same version (handshake at the bottom).
local NAME, VERSION = "bto-goldenitems-sync", "1.0"
local mod = RegisterMod("BTO Golden Items Sync", 1)
local function log(s) Isaac.DebugString("[" .. NAME .. "] " .. s) end

-- Golden Items (Workshop 3338495603, v1.1) rolls "is this pedestal golden?" from GOLDEN_ITEM.GoldChanceRNG, a
-- local seeded from the start seed only on the first pedestal since the game was launched (main.lua:187-216)
-- and never again. A client that already played a run, joined later or came back after a split holds a
-- different RNG state: different golden pedestals, desync. That RNG is out of reach (a local), so this mod
-- decides first, from the pedestal's own InitSeed (synced), and writes the answer where Golden Items looks
-- before rolling (pData.IsGoldenCollectible, main.lua:202-210): its own roll is never drawn again.
-- Same rules as its roll: 0.6% (ITEM_GOLD_CHANCE), or an item already in goldActiveList; never on the
-- Death Certificate floor, on quest items, or on actives (TurnItemGold refuses them, main.lua:154).
local GOLD_CHANCE = 0.006
-- Not Isaac's RNG: it gives nearly equal draws for nearby seeds (x837, x838, x839 -> 0.00546, 0.00552, 0.00558,
-- even after a multiplicative hash consecutive seeds hit 0.9%). MurmurHash3's finalizer: 0.6% on any seeds.
local function roll(seed)
	local h = seed & 0xffffffff
	h = ((h ~ (h >> 16)) * 0x85ebca6b) & 0xffffffff
	h = ((h ~ (h >> 13)) * 0xc2b2ae35) & 0xffffffff
	return (h ~ (h >> 16)) / 4294967296
end
mod:AddPriorityCallback(ModCallbacks.MC_POST_PICKUP_INIT, CallbackPriority.IMPORTANT, function(_, pickup)
	local G = GoldenItems
	if not (G and G.PersistentDataHelper and G.RunSave) then return end
	local pData = G.PersistentDataHelper:GetRerollPersistentData(pickup)
	if pData.IsGoldenCollectible ~= nil or G:HasGoldenItem(pickup.SubType) then return end -- decided already: its rules
	local cfg = Isaac.GetItemConfig():GetCollectible(pickup.SubType)
	local list = G:RunSave().goldActiveList
	local gold = roll(pickup.InitSeed) < GOLD_CHANCE or (list ~= nil and not not list[tostring(pickup.SubType)])
	pData.IsGoldenCollectible = gold and cfg ~= nil and not cfg:HasTags(ItemConfig.TAG_QUEST)
		and cfg.Type ~= ItemType.ITEM_ACTIVE and not G.IsDeathCertificateFloor()
end, PickupVariant.PICKUP_COLLECTIBLE)
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
