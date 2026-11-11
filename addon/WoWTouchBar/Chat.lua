-- WoWTouchBar: whispers and guild chat for the Touch Bar (Phase 4).
-- Read-only: listens to the normal chat events (verified on Forever, feasibility G13); never sends chat.
-- Privacy: messages go straight to the strip and are NEVER stored (not in SavedVariables, not in diagnostics).
-- Messages the client marks secret (e.g. in restricted content) are skipped, never worked around.
local _, ns = ...
local Compat, Text = ns.Compat, ns.Text
local Chat = {}
ns.Chat = Chat

Chat.KIND = { whisper = 1, bnWhisper = 2, guild = 3, guildMention = 4 }
Chat.GUILD_MODES = { off = true, mentions = true, all = true }
Chat.EVENTS = { "CHAT_MSG_WHISPER", "CHAT_MSG_BN_WHISPER", "CHAT_MSG_GUILD" }
Chat.MAX_PENDING = 3 -- busy guild chat must not build a backlog on the strip

--- Current chat settings with defaults: whispers on, guild chat only when you're mentioned.
function Chat.settings(all)
  local c = all.chat or {}
  return { whispers = c.whispers ~= false, guild = Chat.GUILD_MODES[c.guild] and c.guild or "mentions" }
end

--- True when `text` mentions `name` as a whole word (case-insensitive, plain match).
function Chat.mentions(text, name)
  if type(text) ~= "string" or type(name) ~= "string" or name == "" then return false end
  local t, n = " " .. text:lower() .. " ", name:lower()
  local i = 1
  while true do
    local s, e = t:find(n, i, true)
    if not s then return false end
    local before, after = t:sub(s - 1, s - 1), t:sub(e + 1, e + 1)
    if not before:match("[%w]") and not after:match("[%w]") then return true end
    i = e + 1
  end
end

--- Turns a chat event into { kind, sender, text } or nil (filtered out, secret, or empty).
function Chat.fromEvent(event, settings, text, sender)
  text, sender = Compat.plain(text), Compat.plain(sender)
  if type(text) ~= "string" then return nil end
  text = Text.clean(text)
  if text == "" then return nil end
  local kind
  if event == "CHAT_MSG_WHISPER" then
    if not settings.whispers then return nil end
    kind = Chat.KIND.whisper
  elseif event == "CHAT_MSG_BN_WHISPER" then
    if not settings.whispers then return nil end
    kind = Chat.KIND.bnWhisper
  elseif event == "CHAT_MSG_GUILD" then
    if settings.guild == "off" then return nil end
    local me = Compat.call("UnitName", "player")
    local mentioned = Chat.mentions(text, Compat.plain(me))
    if settings.guild == "mentions" and not mentioned then return nil end
    kind = mentioned and Chat.KIND.guildMention or Chat.KIND.guild
  else
    return nil
  end
  local who = ""
  if kind ~= Chat.KIND.bnWhisper and type(sender) == "string" then
    who = Text.clean(Compat.call("Ambiguate", sender, "short") or sender) -- drop "-Realm"
  end
  return { kind = kind, sender = who, text = text }
end

local nextId = 0
--- Event handler: filters the message and hands it to the transport. Nothing is kept afterwards.
function Chat.onEvent(event, text, sender)
  local m = Chat.fromEvent(event, Chat.settings(ns.State.settings()), text, sender)
  if not m then return end
  nextId = (nextId + 1) % 65536
  m.id = nextId
  ns.Transport.sendChat(m)
end
