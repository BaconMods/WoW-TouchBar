-- WoWTouchBar: things worth an alert or a timer - new mail, ready check, dungeon finder queue / proposal.
-- Read-only; APIs and events verified on Forever 2026-11-09 (docs/04_FEASIBILITY.md G12-G13).
-- Never combat information.
local _, ns = ...
local Compat = ns.Compat
local Status = {}
ns.Status = Status

-- Dungeon finder category: the client's constant when it has one, else 1 (dungeon finder).
local function lfdCategory() return rawget(_G, "LE_LFG_CATEGORY_LFD") or 1 end

local queuedSince -- GetTime() when we first saw "queued" (fallback when the queue stats don't say)
-- Values are frozen while the queue / ready check lasts, so the strip doesn't change every refresh:
-- the app counts on from when it first received them.
local frozenQueued, frozenReady

--- Time already spent in the queue, in whole seconds, or nil if unknown.
function Status.queuedSeconds(now, statsQueuedAt)
  local started = statsQueuedAt
  -- Only trust the client's timestamp if it is a plausible past GetTime() value (shape not yet confirmed).
  if type(started) ~= "number" or started <= 0 or started > now or now - started > 86400 then started = queuedSince end
  if not started then return nil end
  return math.max(0, math.floor(now - started))
end

function Status.read()
  local s = {}
  s.newMail = Compat.call("HasNewMail") == true

  local left = Compat.number(Compat.call("GetReadyCheckTimeLeft"))
  s.readyCheck = left ~= nil and left > 0
  if s.readyCheck then
    frozenReady = frozenReady or math.floor(left + 0.5)
    s.readyCheckSeconds = frozenReady
  else
    frozenReady = nil
  end

  local mode = Compat.call("GetLFGMode", lfdCategory())
  if type(mode) ~= "string" then mode = nil end
  s.proposal = mode == "proposal"
  s.queued = mode == "queued"
  local now = Compat.number(Compat.call("GetTime")) or 0
  if s.queued then
    queuedSince = queuedSince or now
    -- 17th value of GetLFGQueueStats is the queue start on Retail; unverified on Forever, so it is only a hint.
    local statsQueuedAt = select(17, Compat.call("GetLFGQueueStats", lfdCategory()))
    frozenQueued = frozenQueued or Status.queuedSeconds(now, Compat.number(statsQueuedAt))
    s.queuedSeconds = frozenQueued
    local _, tank, healer, dps = Compat.call("GetLFGRoles")
    s.tank, s.healer, s.dps = tank == true, healer == true, dps == true
  elseif not s.proposal then
    queuedSince, frozenQueued = nil, nil
  end
  return s
end

-- ---------------------------------------------------------------- /wtb testqueue (pretend, for checking the bar)
-- Makes read() report a dungeon-finder queue (or a dungeon proposal) for a while, as if the game said so.
-- Nothing is queued in the game; for checking the layout without being able to queue (e.g. too low a level).
local simulated -- { untilTime, proposal, startSeconds }

function Status.simulate(kind, seconds)
  local now = Compat.number(Compat.call("GetTime")) or 0
  if kind == "off" then simulated = nil; return end
  simulated = { untilTime = now + (seconds or 60), proposal = kind == "ready", startSeconds = 95 }
end

function Status.simulating() return simulated ~= nil end

local realRead = Status.read
function Status.read()
  local s = realRead()
  if simulated then
    local now = Compat.number(Compat.call("GetTime")) or 0
    if now > simulated.untilTime then
      simulated = nil
    elseif simulated.proposal then
      s.proposal, s.queued = true, false
    else
      s.queued, s.queuedSeconds = true, simulated.startSeconds -- frozen; the app counts on
      if not (s.tank or s.healer or s.dps) then s.dps = true end
    end
  end
  return s
end

-- Test hook: forget remembered queue / ready-check values.
function Status.reset() queuedSince, frozenQueued, frozenReady = nil, nil, nil end
