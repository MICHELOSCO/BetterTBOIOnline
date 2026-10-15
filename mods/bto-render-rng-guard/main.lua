-- BTO Render RNG Guard v1.0 (BetterTBOIOnline). Random numbers drawn while rendering go to a local RNG, never to the synced one.
-- Every player in the party must have the same BTO mods at the same version (handshake at the bottom).
local NAME, VERSION = "bto-render-rng-guard", "1.0"
local mod = RegisterMod("BTO Render RNG Guard", 1)
local game = Game()
local function log(s) Isaac.DebugString("[" .. NAME .. "] " .. s) end

-- Render runs a different number of times on each PC (it depends on fps), so any synced random source
-- consumed inside a render callback drifts the clients apart. Update-time behaviour is unchanged.
local cosmetic = RNG() -- local-only RNG for anything drawn while rendering
cosmetic:SetSeed(1, 35)

-- Render-phase detection. The vanilla dispatcher (_RunCallback in resources/scripts/main.lua) stops at the
-- first handler that errors or returns non-nil, so an "end of render" handler can be skipped (Fair Co-op
-- Items errors in MC_POST_RENDER every session). Hence no counting: every render callback sets the flag,
-- every update-phase callback clears it, as the first handler of each dispatch.
-- ponytail: a mod spawning entities *during render* would clear the flag mid-render (none installed does).
local FIRST = CallbackPriority.IMPORTANT - 100
local inRender = false
local notUpdate = { MC_INPUT_ACTION = true, MC_EXECUTE_CMD = true, MC_EVALUATE_CACHE = true, MC_PRE_MOD_UNLOAD = true }
for cbName, id in pairs(ModCallbacks) do
	if cbName:find("RENDER") or cbName == "MC_GET_SHADER_PARAMS" then
		mod:AddPriorityCallback(id, FIRST, function() inRender = true end)
	elseif not notUpdate[cbName] then
		mod:AddPriorityCallback(id, FIRST, function() inRender = false end)
	end
end

local origRandom, origRandomVector, origMathRandom = Random, RandomVector, math.random
Random = function() if inRender then return cosmetic:Next() end return origRandom() end
RandomVector = function()
	if inRender then return Vector.FromAngle(cosmetic:RandomFloat() * 360) end
	return origRandomVector()
end
-- math.random is a per-process C stream (never synced). Outside render it is re-seeded from
-- (run seed, game frame) on each new frame, so its values depend only on the synced simulation.
local lastFrame = -1
math.random = function(...)
	if not inRender then
		local ok, f = pcall(game.GetFrameCount, game) -- can be called while mods load, before a run exists
		if ok and f ~= lastFrame then
			lastFrame = f
			pcall(function() math.randomseed((game:GetSeeds():GetStartSeed() + f * 7919) % 2147483647) end)
		end
	end
	return origMathRandom(...)
end

-- Helpers of other mods, wrapped as soon as they exist (the order in which mods load is not fixed).
-- StageAPI.Random(a, b) without an explicit rng uses StageAPI.RandomRNG; The Future (v1.8) draws its
-- lightning timer from TheFuture.rng inside MC_POST_RENDER (tf_scripts/thefuture.lua:226).
local doneStageAPI, doneFuture = false, false
local function wrapHelpers()
	if not doneStageAPI and StageAPI and StageAPI.Random then
		local sr, srf = StageAPI.Random, StageAPI.RandomFloat
		StageAPI.Random = function(a, b, rng) if inRender and not rng then rng = cosmetic end return sr(a, b, rng) end
		StageAPI.RandomFloat = function(a, b, rng) if inRender and not rng then rng = cosmetic end return srf(a, b, rng) end
		doneStageAPI = true
		log("StageAPI.Random/RandomFloat guarded")
	end
	if not doneFuture and TheFuture and TheFuture.RandomInt then
		local ri = TheFuture.RandomInt
		TheFuture.RandomInt = function(self, min, max, entrng)
			if inRender and not entrng then entrng = cosmetic end
			return ri(self, min, max, entrng)
		end
		doneFuture = true
		log("TheFuture.RandomInt guarded")
	end
end
wrapHelpers()
mod:AddPriorityCallback(ModCallbacks.MC_POST_GAME_STARTED, FIRST, wrapHelpers)
log("loaded: Random, RandomVector, math.random guarded while rendering")

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
