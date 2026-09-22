-- BetterTBOIOnline: snapshot del personaggio come rete di sicurezza per lo split da desync.
-- In co-op ogni client simula TUTTI i personaggi in modo identico fino al frame del desync.
-- Questa mod fotografa lo stato di ogni giocatore a ogni stanza e lo salva su file (mod SaveData).
-- Se avviene lo split (numero giocatori 2 -> 1), l'ultima foto resta congelata e recuperabile.
-- Sola scrittura su file mod: non tocca il gameplay, non tocca l'RNG, non modifica il gioco.
--
-- Recupero (console, `--luadebug` o mod che apre la console): `bto_restore <indice>` riapplica
-- oggetti, trinket, carte/pillole, cuori e monete/bombe/chiavi salvati a un giocatore locale.
-- ponytail: enumera i collezionabili con un ciclo 1..732 a ogni stanza; se pesa troppo, alzare STRIDE.

local json = require("json")
local mod = RegisterMod("BetterTBOIOnline Snapshot", 1)
local game = Game()

local MAX_COLLECTIBLE = 732
local MAX_CARD = 660
local state = { snaps = {}, frozen = nil }  -- snaps[playerIndex] = tabella; frozen = copia allo split

local function log(s) Isaac.DebugString("[BTO-SNAP] " .. s) end

local function snapshotPlayer(p)
	local items = {}
	for id = 1, MAX_COLLECTIBLE do
		local n = p:GetCollectibleNum(id, true)  -- true: ignora gli effetti temporanei
		if n > 0 then items[tostring(id)] = n end
	end
	local cards, pills = {}, {}
	for slot = 0, 3 do
		local c = p:GetCard(slot)
		if c and c ~= 0 then cards[#cards + 1] = c end
		local pl = p:GetPill(slot)
		if pl and pl ~= 0 then pills[#pills + 1] = pl end
	end
	return {
		type = p:GetPlayerType(),
		name = p:GetName(),
		items = items,
		trinket0 = p:GetTrinket(0),
		trinket1 = p:GetTrinket(1),
		active0 = p:GetActiveItem(ActiveSlot.SLOT_PRIMARY),
		active1 = p:GetActiveItem(ActiveSlot.SLOT_SECONDARY),
		cards = cards,
		pills = pills,
		maxHearts = p:GetMaxHearts(),
		hearts = p:GetHearts(),
		soul = p:GetSoulHearts(),
		bone = p:GetBoneHearts(),
		golden = p:GetGoldenHearts(),
		eternal = p:GetEternalHearts(),
		coins = p:GetNumCoins(),
		bombs = p:GetNumBombs(),
		keys = p:GetNumKeys(),
	}
end

local function snapshotAll(reason)
	local n = game:GetNumPlayers()
	state.snaps = {}
	for i = 0, n - 1 do state.snaps[i] = snapshotPlayer(Isaac.GetPlayer(i)) end
	mod:SaveData(json.encode(state))
	log(string.format("snapshot(%s) players=%d", reason, n))
end

-- Rileva lo split: quando i giocatori scendono, congela l'ultima foto prima del calo.
local lastCount = -1
mod:AddCallback(ModCallbacks.MC_POST_UPDATE, function()
	local n = game:GetNumPlayers()
	if lastCount ~= -1 and n < lastCount then
		state.frozen = { at = Isaac.GetTime(), before = lastCount, after = n, snaps = state.snaps }
		mod:SaveData(json.encode(state))
		log(string.format("SPLIT rilevato %d->%d, foto congelata", lastCount, n))
	end
	lastCount = n
end)

mod:AddCallback(ModCallbacks.MC_POST_NEW_ROOM, function() snapshotAll("new-room") end)
mod:AddCallback(ModCallbacks.MC_POST_GAME_STARTED, function(_, continued)
	lastCount = -1
	if continued then
		local ok, data = pcall(function() return json.decode(mod:LoadData()) end)
		if ok and data then state = data; log("stato ricaricato dal salvataggio") end
	end
	snapshotAll("game-start")
end)

local function restore(p, s)
	if not s then return "nessuna foto" end
	for id, n in pairs(s.items) do
		local want = tonumber(n)
		while p:GetCollectibleNum(tonumber(id), true) < want do
			p:AddCollectible(tonumber(id), 0, false)  -- 0 charge, no first-time animation
		end
	end
	if s.trinket0 and s.trinket0 ~= 0 then p:AddTrinket(s.trinket0, false) end
	if s.trinket1 and s.trinket1 ~= 0 then p:AddTrinket(s.trinket1, false) end
	for _, c in ipairs(s.cards or {}) do p:AddCard(c) end
	for _, pl in ipairs(s.pills or {}) do p:AddPill(pl) end
	p:AddMaxHearts((s.maxHearts or 0) - p:GetMaxHearts(), false)
	p:AddHearts((s.hearts or 0) - p:GetHearts())
	p:AddSoulHearts((s.soul or 0) - p:GetSoulHearts())
	p:AddBoneHearts((s.bone or 0) - p:GetBoneHearts())
	p:AddGoldenHearts((s.golden or 0) - p:GetGoldenHearts())
	p:AddEternalHearts((s.eternal or 0) - p:GetEternalHearts())
	p:AddCoins((s.coins or 0) - p:GetNumCoins())
	p:AddBombs((s.bombs or 0) - p:GetNumBombs())
	p:AddKeys((s.keys or 0) - p:GetNumKeys())
	p:EvaluateItems()
	return string.format("ripristinato %s (type=%d)", s.name or "?", s.type or -1)
end

mod:AddCallback(ModCallbacks.MC_EXECUTE_CMD, function(_, cmd, args)
	if cmd ~= "bto_restore" and cmd ~= "bto_dump" then return end
	local src = (state.frozen and state.frozen.snaps) or state.snaps
	if cmd == "bto_dump" then
		local out = {}
		for i, s in pairs(src) do out[#out + 1] = string.format("p%s=%s type=%d items=%d", i, s.name, s.type, (function() local c = 0 for _ in pairs(s.items) do c = c + 1 end return c end)()) end
		return table.concat(out, "\n")
	end
	local idx = tonumber(args) or 0
	return restore(Isaac.GetPlayer(0), src[idx])
end)

log("loaded")
