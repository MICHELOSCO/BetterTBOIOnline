-- BetterTBOIOnline Diagnostics: read-only logger. Never changes game state, never touches RNG.
-- Output: "[BTO-DIAG]" lines in Documents/My Games/Binding of Isaac Repentance+/log.txt
-- (and in online_logs/sessions/<id>/log.txt when online). f=<frame> matches the "[Frame N]"
-- of the game's own network log lines, so the two can be correlated.
-- ponytail: vanilla Lua has no file I/O without --luadebug, so we piggyback on Isaac.DebugString.

local mod = RegisterMod("BetterTBOIOnline Diagnostics", 1)
local game = Game()

local function log(fmt, ...)
	-- `os` is nil in the vanilla sandbox; t = Isaac.GetTime() (system ms clock).
	Isaac.DebugString(string.format("[BTO-DIAG] t=%d f=%d %s", Isaac.GetTime(), game:GetFrameCount(), string.format(fmt, ...)))
end

local function describe(p)
	return string.format("ctrl=%d type=%d name=%s items=%d ghost=%s",
		p.ControllerIndex, p:GetPlayerType(), p:GetName(), p:GetCollectibleCount(), tostring(p:IsCoopGhost()))
end

local function snapshot(reason)
	local n = game:GetNumPlayers()
	log("snapshot(%s) players=%d", reason, n)
	for i = 0, n - 1 do
		log("  p%d %s", i, describe(Isaac.GetPlayer(i)))
	end
end

-- Polling: vanilla has no join/leave/network callbacks, so detect changes per frame.
local lastCount = -1
local lastItems = {}

mod:AddCallback(ModCallbacks.MC_POST_UPDATE, function()
	local n = game:GetNumPlayers()
	if n ~= lastCount then
		log("player count %d -> %d", lastCount, n)
		lastCount = n
		lastItems = {}
		snapshot("count-change")
	end
	for i = 0, n - 1 do
		local p = Isaac.GetPlayer(i)
		local c = p:GetCollectibleCount()
		if lastItems[i] and lastItems[i] ~= c then
			log("p%d items %d -> %d (%s)", i, lastItems[i], c, describe(p))
		end
		lastItems[i] = c
	end
end)

mod:AddCallback(ModCallbacks.MC_POST_GAME_STARTED, function(_, continued)
	lastCount = -1
	log("game started continued=%s", tostring(continued))
	snapshot("game-start")
end)

mod:AddCallback(ModCallbacks.MC_PRE_GAME_EXIT, function(_, shouldSave)
	log("game exit shouldSave=%s", tostring(shouldSave))
	snapshot("game-exit")
end)

mod:AddCallback(ModCallbacks.MC_POST_GAME_END, function(_, gameOver)
	log("game end gameOver=%s", tostring(gameOver))
end)

mod:AddCallback(ModCallbacks.MC_POST_NEW_LEVEL, function()
	log("new level stage=%d type=%d", game:GetLevel():GetStage(), game:GetLevel():GetStageType())
end)

-- Room changes matter: pending online joins appear to be processed only on room load.
mod:AddCallback(ModCallbacks.MC_POST_NEW_ROOM, function()
	log("new room idx=%d players=%d", game:GetLevel():GetCurrentRoomIndex(), game:GetNumPlayers())
end)

mod:AddCallback(ModCallbacks.MC_POST_PLAYER_INIT, function(_, p)
	log("player init %s", describe(p))
end)

mod:AddCallback(ModCallbacks.MC_POST_ENTITY_REMOVE, function(_, e)
	local p = e:ToPlayer()
	log("player removed %s", p and describe(p) or "?")
end, EntityType.ENTITY_PLAYER)

-- Items dropped when a player leaves show up here as new collectible pedestals.
mod:AddCallback(ModCallbacks.MC_POST_PICKUP_INIT, function(_, pk)
	log("collectible spawned id=%d spawner=%d pos=(%.0f,%.0f)", pk.SubType, pk.SpawnerType, pk.Position.X, pk.Position.Y)
end, PickupVariant.PICKUP_COLLECTIBLE)

-- State trace, once per second: compare the same f= across two clients' logs to find the first
-- frame where they diverge (the game only dumps the frame it notices). fr = TheFuture.rng state.
mod:AddCallback(ModCallbacks.MC_POST_UPDATE, function()
	if game:GetFrameCount() % 30 ~= 0 then return end
	local p = Isaac.GetPlayer(0)
	local fr = TheFuture and TheFuture.rng and TheFuture.rng:GetSeed() or 0
	log("trace room=%d ents=%d p0=(%.1f,%.1f) fr=%d", game:GetLevel():GetCurrentRoomIndex(),
		#Isaac.GetRoomEntities(), p.Position.X, p.Position.Y, fr)
end)

-- v0.3: per-NPC seeds. 07/10 desync: Rubber's 5-projectile death ring was rotated differently on the two
-- PCs while every other entity and the global RNG were identical, so the divergence lived in that one
-- enemy's own RNG. Logging InitSeed and the drop-RNG state at init and at death on both clients tells
-- "born with a different seed" apart from "RNG consumed a different number of times".
local function npcInfo(npc)
	return string.format("%d.%d.%d seed=%d rng=%d pos=(%.0f,%.0f)", npc.Type, npc.Variant, npc.SubType,
		npc.InitSeed, npc:GetDropRNG():GetSeed(), npc.Position.X, npc.Position.Y)
end
mod:AddCallback(ModCallbacks.MC_POST_NPC_INIT, function(_, npc) log("npc init %s", npcInfo(npc)) end)
mod:AddCallback(ModCallbacks.MC_POST_NPC_DEATH, function(_, npc) log("npc death %s", npcInfo(npc)) end)
-- Projectiles fired by The Future's enemies (type 391): frame and angle of each shot, to compare the ring.
mod:AddCallback(ModCallbacks.MC_POST_PROJECTILE_INIT, function(_, proj)
	if proj.SpawnerType == 391 then
		log("proj spawner=%d.%d angle=%.1f", proj.SpawnerType, proj.SpawnerVariant, proj.Velocity:GetAngleDegrees())
	end
end)

log("loaded v0.3")

-- Snapshot DISATTIVATO (22/09/2026): crash nativo in GetCollectibleNum durante una transizione
-- di stanza, che ha fatto crashare entrambi i client in lockstep. Vedi docs/findings.md.
-- Non riattivare finché non è corretto e testato.
-- include("snapshot")
