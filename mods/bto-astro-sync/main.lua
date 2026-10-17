-- BTO Astro Sync v1.1 (BetterTBOIOnline). Astrobirth (Astro Items) without its client-local inputs. Needs Astrobirth.
-- Every player in the party must have the same BTO mods at the same version (handshake at the bottom).
local NAME, VERSION = "bto-astro-sync", "1.1"
local mod = RegisterMod("BTO Astro Sync", 1)
local function log(s) Isaac.DebugString("[" .. NAME .. "] " .. s) end

-- Astrobirth (Workshop 3260980911, v1.288) acts on inputs only the pressing PC sees: raw keys (P Key: P,
-- Legacy: 7, Astrophage and My Moon My Man: 8, Taurus EX's bind), the mouse (Overmind, Dry Eye Syndrome) and
-- "hold Drop on controller 0 for 60 rendered frames" (astro/upgrade-action.lua, marigold.lua), which turns
-- vanilla pedestals into Astro items (Chaos + Succubus, Guppy + Brimstone/rotten/eternal hearts, Black Candle
-- + Libra, 6 sets + Birthright). Seen live 17/10: pressing P with R Key took R Key and spawned P Key on that
-- PC only. While any Astro callback runs (766 of them, registered under Astro.DDSMyMod) those reads answer
-- "not pressed"; every other mod reads input as before.
-- Only with 2+ players at run start (see partySize): playing alone, Astrobirth stays complete.
-- ponytail: a vanilla callback fired from inside an Astro one (e.g. its UseActiveItem) also sees no raw keys.
local inAstro, active = 0, false
local I = Input
local btnT, btnP, mouseP, actVal = I.IsButtonTriggered, I.IsButtonPressed, I.IsMouseBtnPressed, I.GetActionValue
I.IsButtonTriggered = function(...) if active and inAstro > 0 then return false end return btnT(...) end
I.IsButtonPressed = function(...) if active and inAstro > 0 then return false end return btnP(...) end
I.IsMouseBtnPressed = function(...) if active and inAstro > 0 then return false end return mouseP(...) end
I.GetActionValue = function(action, ctrl)
	if active and inAstro > 0 and action == ButtonAction.ACTION_DROP and ctrl == 0 then return 0 end
	return actVal(action, ctrl)
end

local wrapped = setmetatable({}, { __mode = "k" })
local function wrapAstro()
	local m = Astro and Astro.DDSMyMod
	if not m then return end
	local n = 0
	for _, id in pairs(ModCallbacks) do
		for _, c in ipairs(Isaac.GetCallbacks(id)) do
			if c.Mod == m and not wrapped[c.Function] then
				local f = c.Function
				local w = function(...)
					inAstro = inAstro + 1
					local ok, r = pcall(f, ...) -- the dispatcher keeps only the first return value
					inAstro = inAstro - 1
					if not ok then error(r, 0) end
					return r
				end
				wrapped[w] = true
				c.Function = w
				n = n + 1
			end
		end
	end
	if n > 0 then log("input guard on " .. n .. " Astro callbacks") end
end

-- Items whose effect runs in render callbacks (a different number of times on each PC) or is driven by the
-- inputs above: out of every item pool, the same way on every PC. Copernicus moves its orbiting tears and
-- counts its cooldown per rendered frame; Zoltraak fires its laser and SnowGrave spawns effects and toggles
-- the controls from render; Decisive Strike's skill check, Leo EX and Taurus EX dashes, Dry Eye Syndrome
-- and the Mega dice menu (Mega D4/D6/D8, Hyper Quantum Dice, Solar System) read input while rendering;
-- P Key, Legacy, Astrophage, My Moon My Man and Overmind would just stop working.
local BANNED = { "COPERNICUS", "ZOLTRAAK", "SNOWGRAVE", "DECISIVE_STRIKE", "LEO_EX", "TAURUS_EX", "DRY_EYE_SYNDROME",
	"HYPER_QUANTUM_DICE", "MEGA_D4", "MEGA_D6", "MEGA_D8", "SOLAR_SYSTEM",
	"P_KEY", "LEGACY", "ASTROPHAGE", "MY_MOON_MY_MAN", "OVERMIND" }

-- People in the run, the same count on every PC: Esau, Tainted Forgotten's soul and Strawman are second
-- bodies of one player (Jacob & Esau alone must not count as two). Local co-op counts too: harmless.
local function partySize()
	local n = 0
	for i = 0, Game():GetNumPlayers() - 1 do
		local p = Isaac.GetPlayer(i)
		if GetPtrHash(p:GetMainTwin()) == GetPtrHash(p) and not p.Parent then n = n + 1 end
	end
	return n
end

mod:AddPriorityCallback(ModCallbacks.MC_POST_GAME_STARTED, CallbackPriority.IMPORTANT - 1, function()
	if not Astro then return end
	-- Continuing a run saved without Astro's data (a join on a PC whose Astro save differs, or Astro added
	-- mid-run) errors in lucky-rock-bottom.lua:34, and an error stops the whole MC_POST_GAME_STARTED dispatch
	-- (resources/scripts/main.lua:71-76): every later handler, BTO ones included, was skipped on that PC only.
	if Astro.Data then
		Astro.Data.PersistentPlayerData = Astro.Data.PersistentPlayerData or {}
		Astro.Data.PersistentPickupData = Astro.Data.PersistentPickupData or {}
	end
	wrapAstro()
	-- re-checked at every run start, continues included: a join in progress reloads the run on every PC
	active = partySize() >= 2
	if not active then log("alone: Astrobirth unchanged") return end
	local pool, n = Game():GetItemPool(), 0
	for _, k in ipairs(BANNED) do
		local id = Astro.Collectible and Astro.Collectible[k]
		if id and id > 0 then pool:RemoveCollectible(id) n = n + 1 end
	end
	log("removed " .. n .. "/" .. #BANNED .. " items from the pools")
end)
mod:AddCallback(ModCallbacks.MC_POST_NEW_ROOM, wrapAstro) -- callbacks Astro registers later on
wrapAstro() -- the order in which mods load is not fixed: also at run start
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
