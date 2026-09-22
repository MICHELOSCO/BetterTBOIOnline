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

log("loaded v0.1")
