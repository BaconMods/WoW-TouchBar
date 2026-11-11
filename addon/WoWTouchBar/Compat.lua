-- WoWTouchBar: client detection + safe API access. Every game call in the addon goes through here,
-- so a missing or changed API degrades one value instead of raising a Lua error.
-- API names used by the modules were verified in Forever 1.60.1 (docs/04_FEASIBILITY.md G1-G11).
local _, ns = ...
local Compat = {}
ns.Compat = Compat

--- Resolves "A.B" against the global table without erroring; nil when absent.
function Compat.lookup(name)
  local value = _G
  for part in name:gmatch("[^%.]+") do
    if type(value) ~= "table" then return nil end
    value = value[part]
  end
  return value
end

function Compat.has(name) return type(Compat.lookup(name)) == "function" end

--- Values the client marks secret (issecretvalue) can't be inspected by addons; treat as unknown.
function Compat.plain(v)
  local check = rawget(_G, "issecretvalue")
  if v ~= nil and type(check) == "function" then
    local ok, secret = pcall(check, v)
    if not ok or secret then return nil end
  end
  return v
end

--- Calls a global API by name. Returns the call's values with secrets replaced by nil,
--- or nothing at all if the API is missing or errors.
local function pack(...) return { n = select("#", ...), ... } end

function Compat.call(name, ...)
  local fn = Compat.lookup(name)
  if type(fn) ~= "function" then return end
  local r = pack(pcall(fn, ...))
  if not r[1] then return end
  local out = { n = r.n - 1 }
  for i = 1, out.n do out[i] = Compat.plain(r[i + 1]) end
  return unpack(out, 1, out.n)
end

--- Number or nil (never a string / secret / NaN).
function Compat.number(v)
  v = Compat.plain(v)
  if type(v) == "number" and v == v then return v end
  return nil
end

--- "forever" (interface 1.x / project 18), "retail" (mainline), or "unknown".
function Compat.flavour()
  local interface = select(4, Compat.call("GetBuildInfo"))
  local project, mainline = rawget(_G, "WOW_PROJECT_ID"), rawget(_G, "WOW_PROJECT_MAINLINE")
  if project == 18 or (type(interface) == "number" and interface >= 10000 and interface < 20000) then return "forever" end
  if project ~= nil and project == mainline then return "retail" end
  return "unknown"
end
