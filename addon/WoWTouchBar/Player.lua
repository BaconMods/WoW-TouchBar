-- WoWTouchBar: level, XP and rested XP (read-only).
local _, ns = ...
local Compat = ns.Compat
local Player = {}
ns.Player = Player

--- XP progress in tenths of a percent (0-1000); 0 when unknown or at max level (xpMax <= 0).
function Player.xpPermille(xp, xpMax)
  if type(xp) ~= "number" or type(xpMax) ~= "number" or xpMax <= 0 then return 0 end
  local p = math.floor(xp * 1000 / xpMax)
  if p < 0 then return 0 end
  if p > 1000 then return 1000 end
  return p
end

function Player.read()
  local level = Compat.number(Compat.call("UnitLevel", "player"))
  local xp = Compat.number(Compat.call("UnitXP", "player"))
  local xpMax = Compat.number(Compat.call("UnitXPMax", "player"))
  local rested = Compat.number(Compat.call("GetXPExhaustion")) or 0 -- nil when not rested
  local maxLevel = Compat.number(Compat.call("GetMaxPlayerLevel"))
  local xpDisabled = Compat.call("IsXPUserDisabled") == true

  local isMaxLevel = (level ~= nil and maxLevel ~= nil and level >= maxLevel) or (xpMax ~= nil and xpMax <= 0)
  local permille = Player.xpPermille(xp, xpMax)
  local restedPermille = 0
  if xpMax and xpMax > 0 then
    -- rested can exceed the current bar (up to 1.5 levels); cap the display at a full bar
    restedPermille = math.min(math.floor(rested * 1000 / xpMax), 1000)
  end

  return {
    level = level,
    maxLevel = maxLevel,
    xp = xp,
    xpMax = xpMax,
    xpPermille = isMaxLevel and 0 or permille,
    rested = rested,
    restedPermille = isMaxLevel and 0 or restedPermille,
    isMaxLevel = isMaxLevel,
    xpDisabled = xpDisabled,
  }
end
