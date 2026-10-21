-- BTO The Future Sync v1.1 (BetterTBOIOnline). Desync fixes specific to The Future (enemies, Steven's room). Needs The Future.
-- Every player in the party must have the same BTO mods at the same version (handshake at the bottom).
local NAME, VERSION = "bto-thefuture-sync", "1.1"
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
-- Steven's room (stevennpc.lua:223-316): on Caves/Catacombs II, 33% of the time (+10% with Steven) a 1x1
-- dead end becomes Steven's room, the way into The Future. Both the roll and the choice of dead end used a
-- module RNG() never seeded (stevennpc.lua:2), whose state depends on each PC's history: desync 19/10 21:51,
-- one PC of three entered "Steven's Cold Storage" where the other two had a normal room (frame 29135).
-- Its MakeStevenRoom becomes a no-op (its roll still runs, harmlessly) and the same decision is made here
-- from the stage seed, identical on every PC.
local stevenWrapped = false
local function wrapSteven()
	if stevenWrapped or not (TheFuture and TheFuture.MakeStevenRoom and StageAPI and StageAPI.RoomsLists) then return end
	TheFuture.MakeStevenRoom = function() end
	stevenWrapped = true
	log("Steven room seeded from the stage")
end

local function isDeadEnd(level, idx)
	local n = 0
	for _, shift in ipairs({ 1, -1, 13, -13 }) do
		if level:GetRoomByIdx(idx + shift).Data then n = n + 1 end
	end
	return n == 1
end

mod:AddCallback(ModCallbacks.MC_POST_NEW_LEVEL, function()
	if not stevenWrapped then return end
	local tf, level = TheFuture, game:GetLevel()
	-- same conditions as stevennpc.lua:229-230
	if game:IsGreedMode() or Isaac.GetChallenge() ~= Challenge.CHALLENGE_NULL or level:GetStage() ~= LevelStage.STAGE2_2
		or (StageAPI.InOverriddenStage() and not (StageAPI.GetCurrentStage().Name == "Catacombs 2"
			or StageAPI.GetCurrentStage().StageHPNumber == 4)) then return end
	local rng = RNG()
	rng:SetSeed(math.max(game:GetSeeds():GetStageSeed(level:GetStage()), 1), 35)
	local chance = tf.StevenChance + (tf:IsPlayerWithCollectible(CollectibleType.COLLECTIBLE_STEVEN) and 0.1 or 0)
	if rng:RandomFloat() > chance then return end
	local ends, rooms = {}, level:GetRooms()
	for i = 0, #rooms - 1 do
		local desc = rooms:Get(i)
		if desc.Data.Shape == RoomShape.ROOMSHAPE_1x1 and desc.Data.Type == RoomType.ROOM_DEFAULT
			and desc.Data.Subtype == 0 and isDeadEnd(level, desc.SafeGridIndex) then
			ends[#ends + 1] = desc.SafeGridIndex
		end
	end
	if #ends == 0 then return end
	local desc = level:GetRoomByIdx(ends[rng:RandomInt(#ends) + 1])
	desc.Data = StageAPI.GetGotoDataForTypeShape(RoomType.ROOM_DEFAULT, RoomShape.ROOMSHAPE_1x1)
	StageAPI.SetLevelRoom(StageAPI.LevelRoom{
		RoomType = RoomType.ROOM_DEFAULT,
		RequireRoomType = false,
		RoomsList = StageAPI.RoomsLists.FutureStevenRooms,
		RoomDescriptor = desc
	}, desc.ListIndex)
	log("Steven room at " .. desc.SafeGridIndex)
end)

local function wrapAll() wrap() wrapSteven() end
wrapAll() -- the order in which mods load is not fixed: retry at run start
mod:AddPriorityCallback(ModCallbacks.MC_POST_GAME_STARTED, CallbackPriority.IMPORTANT, wrapAll)
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
