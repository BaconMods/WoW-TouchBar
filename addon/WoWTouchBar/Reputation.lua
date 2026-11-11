-- WoWTouchBar: the watched reputation (Reputation panel → "Show as experience bar"). Read-only.
-- C_Reputation.GetWatchedFactionData is present on Forever (G12); its fields are read defensively because the
-- return shape has not been captured on Forever yet - anything missing becomes "unknown", never a guess.
local _, ns = ...
local Compat = ns.Compat
local Reputation = {}
ns.Reputation = Reputation

local function num(t, key) return Compat.number(t[key]) end

function Reputation.read()
  local d = Compat.call("C_Reputation.GetWatchedFactionData")
  if type(d) ~= "table" then return { none = true } end
  local name = Compat.plain(d.name)
  local id = num(d, "factionID")
  if type(name) ~= "string" or name == "" then return { none = true } end

  local r = { factionID = id, name = name, reaction = num(d, "reaction") }
  local low, high, cur = num(d, "currentReactionThreshold"), num(d, "nextReactionThreshold"), num(d, "currentStanding")
  if low and high and cur and high > low then
    r.value = math.max(0, math.min(cur - low, high - low))
    r.max = high - low
  end
  if r.reaction then
    local label = rawget(_G, "FACTION_STANDING_LABEL" .. r.reaction)
    if type(label) == "string" then r.standing = label end
  end
  if id and Compat.call("C_Reputation.IsMajorFaction", id) == true then
    r.standing = "Renown"
    r.value, r.max = nil, nil -- renown progress lives in another API (not used)
  end
  return r
end
