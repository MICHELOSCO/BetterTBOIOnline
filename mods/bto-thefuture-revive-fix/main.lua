-- BTO The Future Revive Fix v1.0 (BetterTBOIOnline). Online, the Revive Machine works inside The Future. Needs The Future.
-- Every player in the party must have the same BTO mods at the same version (handshake at the bottom).
local NAME, VERSION = "bto-thefuture-revive-fix", "1.0"
local mod = RegisterMod("BTO The Future Revive Fix", 1)
local game = Game()
local function log(s) Isaac.DebugString("[" .. NAME .. "] " .. s) end

-- 09/10 00:47: in The Future, after donating to the Revive Machine nobody could pick who to revive and the
-- game ignored every input. The Future tints its HUD by drawing it itself inside its shader handler:
-- hud:SetVisible(true); hud:Render(); hud:SetVisible(false) (tf_scripts/shaders.lua:66-68), so the HUD
-- "visible" byte (Game+0x22ed0 = HUD+0x72f8) is 0 during every game update. The Revive Player menu
-- (gfx/ui/Menu_RevivePlayer.anm2, Game+0x24ecc, opened by the machine) only updates and draws while that
-- byte is 1 (0x8EF9AF / 0x8EFE5F), and the game drops all player input while the menu is open (0x900CF0).
-- With 2+ players, inside The Future's handler SetVisible(false) and the manual Render() are skipped: the
-- game draws the (untinted) HUD itself and the menu works. Single player untouched.
local FIRST = CallbackPriority.IMPORTANT - 100
local tfShader = false
mod:AddPriorityCallback(ModCallbacks.MC_GET_SHADER_PARAMS, FIRST, function(_, shader)
	tfShader = shader == "TFColorCorrection" and game:GetNumPlayers() >= 2
end)
for _, cb in ipairs({ ModCallbacks.MC_POST_RENDER, ModCallbacks.MC_POST_UPDATE }) do
	mod:AddPriorityCallback(cb, FIRST, function() tfShader = false end)
end

local meta = getmetatable(game:GetHUD())
local oldIndex = type(meta) == "table" and rawget(meta, "__index")
if type(oldIndex) == "function" then
	local function setVisible(self, visible)
		if tfShader and not visible then return end
		return oldIndex(self, "SetVisible")(self, visible)
	end
	local function render(self)
		if tfShader then return end
		return oldIndex(self, "Render")(self)
	end
	rawset(meta, "__index", function(self, k)
		if k == "SetVisible" then return setVisible end
		if k == "Render" then return render end
		return oldIndex(self, k)
	end)
	log("loaded: HUD SetVisible/Render wrapped")
else
	log("HUD metatable not reachable, fix off")
end

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
