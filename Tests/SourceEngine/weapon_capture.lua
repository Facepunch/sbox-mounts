-- Install under garrysmod/lua/autorun/server on a dedicated Sandbox server with a free player slot.
-- Run s1_weapon_capture <classname> and read garrysmod/data/s1_weapon_capture/<classname>.json.
local fixture
local tag = "s1_weapon_capture"

local function finish(reason)
    local f = fixture
    fixture = nil
    if not f then return end
    f.trace.error = reason
    file.CreateDir(tag)
    file.Write(tag .. "/" .. f.trace.class .. ".json", util.TableToJSON(f.trace))
    if IsValid(f.player) then f.player:Kick("Weapon capture finished") end
    print(tag, f.trace.class, reason or "complete", #f.trace.ticks)
end

concommand.Add(tag, function(caller, _, args)
    if IsValid(caller) or fixture then return end
    local class = args[1]
    if not class or not string.match(class, "^weapon_[%w_]+$") then return end
    local p = player.CreateNextBot("WeaponCapture")
    if not IsValid(p) then print(tag, "could not allocate test player") return end
    p:Spawn()
    p:StripWeapons()
    p:StripAmmo()
    local w = p:Give(class)
    if not IsValid(w) then p:Kick("Unknown weapon") return end
    local ammo = w:GetPrimaryAmmoType()
    local pickupReserve = p:GetAmmoCount(ammo)
    if ammo >= 0 then p:SetAmmo(64, ammo) end
    p:SelectWeapon(class)
    fixture = {player = p, weapon = w, created = engine.TickCount(), trace = {
        map = game.GetMap(), build = VERSIONSTR, gamemode = engine.ActiveGamemode(),
        pickupReserve = pickupReserve, interval = engine.TickInterval(), class = class, ticks = {}, sounds = {}
    }}
end)

hook.Add("StartCommand", tag, function(p, cmd)
    local f = fixture
    if not f or p ~= f.player then return end
    cmd:ClearMovement()
    cmd:ClearButtons()
    cmd:SetViewAngles(Angle(0, 0, 0))
    if p:GetActiveWeapon() ~= f.weapon then
        if engine.TickCount() - f.created > 128 then finish("weapon selection timed out") end
        return
    end
    f.start = f.start or engine.TickCount()
    local tick = engine.TickCount() - f.start
    local elapsed = tick * f.trace.interval
    local buttons = 0
    if elapsed >= 1 and elapsed < 2 then buttons = IN_ATTACK
    elseif tick == 145 or tick == 155 then buttons = IN_ATTACK
    elseif elapsed >= 3 and elapsed < 3 + f.trace.interval then buttons = IN_RELOAD end
    cmd:SetButtons(buttons)
    f.buttons = buttons
end)

hook.Add("FinishMove", tag, function(p)
    local f = fixture
    if not f or p ~= f.player or not f.start then return end
    local w = f.weapon
    if not IsValid(w) then finish("weapon removed") return end
    local tick = engine.TickCount() - f.start
    local vm = p:GetViewModel()
    local punch = p:GetViewPunchAngles()
    local velocity = p:GetViewPunchVelocity()
    table.insert(f.trace.ticks, {tick = tick, input = f.buttons, primary = w:Clip1(), secondary = w:Clip2(),
        punch = {punch.p, punch.y, punch.r}, punchVelocity = {velocity.p, velocity.y, velocity.r},
        reserve = p:GetAmmoCount(w:GetPrimaryAmmoType()), sequence = IsValid(vm) and vm:GetSequenceName(vm:GetSequence()),
        cycle = IsValid(vm) and vm:GetCycle(), nextPrimary = w:GetNextPrimaryFire() - CurTime()})
    if tick * f.trace.interval >= 6 then finish() end
end)

hook.Add("EntityEmitSound", tag, function(sound)
    local f = fixture
    if not f or not f.start then return end
    if sound.Entity == f.weapon or sound.Entity == f.player or sound.Entity == f.player:GetViewModel() then
        table.insert(f.trace.sounds, {tick = engine.TickCount() - f.start, name = sound.OriginalSoundName, wave = sound.SoundName})
    end
end)
