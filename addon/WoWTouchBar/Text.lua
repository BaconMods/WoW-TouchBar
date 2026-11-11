-- WoWTouchBar: text helpers. UTF-8 safe (quest names can contain non-ASCII characters).
local _, ns = ...
local Text = {}
ns.Text = Text

--- Number of UTF-8 characters (invalid bytes count as one character each).
function Text.length(s)
  local n = 0
  for _ in s:gmatch("[^\128-\191][\128-\191]*") do n = n + 1 end
  return n
end

--- Cuts `s` to at most `max` characters without splitting a multi-byte character.
--- Adds "..." when shortened (counted within `max`).
function Text.truncate(s, max)
  if type(s) ~= "string" then return "" end
  if max <= 0 then return "" end
  if Text.length(s) <= max then return s end
  local keep = math.max(max - 3, 0)
  local out, n = {}, 0
  for ch in s:gmatch("[^\128-\191][\128-\191]*") do
    if n >= keep then break end
    out[#out + 1] = ch
    n = n + 1
  end
  local cut = table.concat(out):gsub("%s+$", "")
  return cut .. ("..."):sub(1, max - keep)
end

--- Removes WoW UI escape codes (colours, textures, atlas icons; hyperlinks keep their text) and control characters.
function Text.clean(s)
  if type(s) ~= "string" then return "" end
  s = s:gsub("|c%x%x%x%x%x%x%x%x", ""):gsub("|r", "")
  s = s:gsub("|H.-|h(.-)|h", "%1"):gsub("|T.-|t", ""):gsub("|A.-|a", "")
  s = s:gsub("|K.-|k", "") -- protected Battle.net names: the client never shows addons the real text
  s = s:gsub("[%c]", " "):gsub("%s+", " "):gsub("^%s+", ""):gsub("%s+$", "")
  return s
end

--- Cuts `s` to at most `maxBytes` bytes on a UTF-8 character boundary (no ellipsis; for the wire format).
function Text.truncateBytes(s, maxBytes)
  if type(s) ~= "string" then return "" end
  if #s <= maxBytes then return s end
  local out, used = {}, 0
  for ch in s:gmatch("[^\128-\191][\128-\191]*") do
    if used + #ch > maxBytes then break end
    out[#out + 1] = ch
    used = used + #ch
  end
  return table.concat(out)
end

--- "1234567" copper → "123g 45s 67c" (omits leading zero units).
function Text.money(copper)
  copper = math.floor(tonumber(copper) or 0)
  local g, s, c = math.floor(copper / 10000), math.floor(copper / 100) % 100, copper % 100
  if g > 0 then return ("%dg %ds %dc"):format(g, s, c) end
  if s > 0 then return ("%ds %dc"):format(s, c) end
  return ("%dc"):format(c)
end

--- Removes the count from objective text: "6/6 Prairie Wolf Paw" / "Wolf slain: 3/8" → the label.
function Text.objectiveLabel(text)
  if type(text) ~= "string" then return "" end
  local label = text:gsub("^%s*%d+%s*/%s*%d+%s+", ""):gsub("[:%s]*%d+%s*/%s*%d+%s*$", "")
  return label
end
