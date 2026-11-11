-- WoWTouchBar: collects the normalised state from every module.
-- A module that errors is disabled (until /reload) and its error saved; the others keep working.
-- Game events only mark the state dirty; a short debounce coalesces bursts (e.g. many BAG_UPDATEs).
local _, ns = ...
local Compat = ns.Compat
local State = {}
ns.State = State

State.DEBOUNCE_SECONDS = 0.25
State.MAX_ERRORS = 20
State.MODULES = { "Player", "Quests", "Utility", "Status", "Reputation", "Display", "Notes" }

State.EVENTS = {
  "PLAYER_ENTERING_WORLD", "PLAYER_XP_UPDATE", "PLAYER_LEVEL_UP", "UPDATE_EXHAUSTION", "PLAYER_UPDATE_RESTING",
  "QUEST_LOG_UPDATE", "QUEST_WATCH_UPDATE", "QUEST_WATCH_LIST_CHANGED", "QUEST_TURNED_IN", "QUEST_ACCEPTED",
  "QUEST_REMOVED", "SUPER_TRACKING_CHANGED", "UNIT_QUEST_LOG_CHANGED",
  "PLAYER_MONEY", "BAG_UPDATE_DELAYED", "UPDATE_INVENTORY_DURABILITY",
  "UPDATE_PENDING_MAIL", "READY_CHECK", "READY_CHECK_FINISHED", "LFG_UPDATE", "LFG_QUEUE_STATUS_UPDATE",
  "LFG_PROPOSAL_SHOW", "LFG_PROPOSAL_FAILED", "LFG_PROPOSAL_SUCCEEDED", "UPDATE_FACTION",
}

local disabled = {}
local pending = false
State.current = nil
State.listeners = {}

local function db()
  WoWTouchBarDB = WoWTouchBarDB or {}
  WoWTouchBarDB.settings = WoWTouchBarDB.settings or {}
  return WoWTouchBarDB
end

function State.settings() return db().settings end

function State.recordError(where, err)
  local d = db()
  d.errors = d.errors or {}
  d.errors[#d.errors + 1] = { time = time(), where = where, err = tostring(err) }
  while #d.errors > State.MAX_ERRORS do table.remove(d.errors, 1) end
end

function State.collect()
  local state = { time = time(), flavour = Compat.flavour(), disabled = {} }
  for _, name in ipairs(State.MODULES) do
    if disabled[name] then
      state.disabled[#state.disabled + 1] = name
    else
      local ok, result = pcall(ns[name].read, State.settings())
      if ok then
        state[name:lower()] = result
      else
        disabled[name] = true
        state.disabled[#state.disabled + 1] = name
        State.recordError(name, result)
      end
    end
  end
  State.current = state
  db().state = state -- diagnostics, written to disk on /reload / logout
  for _, fn in ipairs(State.listeners) do
    local ok, err = pcall(fn, state)
    if not ok then State.recordError("listener", err) end
  end
  return state
end

--- Coalesces bursts of events into one collect.
function State.markDirty()
  if pending then return end
  pending = true
  C_Timer.After(State.DEBOUNCE_SECONDS, function()
    pending = false
    State.collect()
  end)
end

function State.onChange(fn) State.listeners[#State.listeners + 1] = fn end

--- Registers the events this module listens to; unknown events are skipped (older/newer clients).
function State.register(frame)
  for _, e in ipairs(State.EVENTS) do pcall(frame.RegisterEvent, frame, e) end
end

function State.isDisabled(name) return disabled[name] == true end
