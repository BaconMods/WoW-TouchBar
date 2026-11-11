-- WoWTouchBar: gold, bag space, durability (read-only).
local _, ns = ...
local Compat = ns.Compat
local Utility = {}
ns.Utility = Utility

Utility.BAGS = { 0, 1, 2, 3, 4 }                                       -- backpack + 4 bag slots
Utility.GEAR_SLOTS = { 1, 3, 5, 6, 7, 8, 9, 10, 16, 17, 18 }           -- slots that can have durability

function Utility.read()
  local free, total = 0, 0
  local bagsKnown = false
  for _, bag in ipairs(Utility.BAGS) do
    local slots = Compat.number(Compat.call("C_Container.GetContainerNumSlots", bag))
    local f = Compat.number((Compat.call("C_Container.GetContainerNumFreeSlots", bag)))
    if slots then
      bagsKnown = true
      total = total + slots
      free = free + (f or 0)
    end
  end

  local lowest, broken, items = nil, 0, 0
  for _, slot in ipairs(Utility.GEAR_SLOTS) do
    local cur, max = Compat.call("GetInventoryItemDurability", slot)
    cur, max = Compat.number(cur), Compat.number(max)
    if cur and max and max > 0 then
      items = items + 1
      local pct = math.floor(cur * 100 / max)
      if not lowest or pct < lowest then lowest = pct end
      if cur == 0 then broken = broken + 1 end
    end
  end

  return {
    money = Compat.number(Compat.call("GetMoney")),
    bagsFree = bagsKnown and free or nil,
    bagsTotal = bagsKnown and total or nil,
    durabilityLowest = lowest,     -- percent of the most worn item; nil when nothing has durability
    durabilityBroken = broken,
    durabilityItems = items,
  }
end

