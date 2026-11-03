# -*- coding: utf-8 -*-
"""Builds the CompositeBody OSC integration manual."""

import os
from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (BaseDocTemplate, Frame, PageTemplate, Paragraph,
                                Spacer, Table, TableStyle, KeepTogether, PageBreak)

OUT = r"C:\_Projects\202611CompositeBody\CompositeBody\CompositeBody\Docs\CompositeBody_OSC_Manual.pdf"

pdfmetrics.registerFont(TTFont("CJK", r"C:\Windows\Fonts\msjh.ttc", subfontIndex=0))
pdfmetrics.registerFont(TTFont("CJK-B", r"C:\Windows\Fonts\msjhbd.ttc", subfontIndex=0))
pdfmetrics.registerFont(TTFont("Mono", r"C:\Windows\Fonts\consola.ttf"))
pdfmetrics.registerFont(TTFont("Mono-B", r"C:\Windows\Fonts\consolab.ttf"))
pdfmetrics.registerFontFamily("CJK", normal="CJK", bold="CJK-B", italic="CJK", boldItalic="CJK-B")

INK = colors.HexColor("#1A1A1A")
MUTED = colors.HexColor("#6B6B6B")
RULE = colors.HexColor("#D8D8D8")
ACCENT = colors.HexColor("#2F5D8C")
BAND = colors.HexColor("#EEF2F6")
CODEBG = colors.HexColor("#F5F5F3")
WARNBG = colors.HexColor("#FDF4E7")
WARNEDGE = colors.HexColor("#C98A2E")

S = {
    "title": ParagraphStyle("title", fontName="CJK-B", fontSize=25, leading=32, textColor=INK),
    "subtitle": ParagraphStyle("subtitle", fontName="CJK", fontSize=12.5, leading=19, textColor=MUTED, wordWrap="CJK"),
    "h1": ParagraphStyle("h1", fontName="CJK-B", fontSize=16, leading=22, textColor=INK,
                         spaceBefore=2, spaceAfter=7),
    "h2": ParagraphStyle("h2", fontName="CJK-B", fontSize=11.8, leading=17, textColor=ACCENT,
                         spaceBefore=12, spaceAfter=5),
    "body": ParagraphStyle("body", fontName="CJK", fontSize=9.6, leading=15.4, textColor=INK,
                           alignment=TA_LEFT, spaceAfter=6, wordWrap="CJK"),
    "note": ParagraphStyle("note", fontName="CJK", fontSize=9.1, leading=14.6, textColor=INK, wordWrap="CJK"),
    "cell": ParagraphStyle("cell", fontName="CJK", fontSize=8.5, leading=12.4, textColor=INK, wordWrap="CJK"),
    "cellb": ParagraphStyle("cellb", fontName="CJK-B", fontSize=8.5, leading=12.4, textColor=INK, wordWrap="CJK"),
    "mono": ParagraphStyle("mono", fontName="Mono", fontSize=8.3, leading=12.2, textColor=INK),
    "monohead": ParagraphStyle("monohead", fontName="Mono-B", fontSize=8.5, leading=12.4,
                               textColor=colors.white),
    "code": ParagraphStyle("code", fontName="Mono", fontSize=8.4, leading=13.2, textColor=INK),
    "head": ParagraphStyle("head", fontName="CJK-B", fontSize=8.6, leading=12.4,
                           textColor=colors.white, wordWrap="CJK"),
    "codecjk": ParagraphStyle("codecjk", fontName="CJK", fontSize=8.6, leading=13.6, textColor=INK),
    "foot": ParagraphStyle("foot", fontName="CJK", fontSize=7.8, leading=11, textColor=MUTED, wordWrap="CJK"),
}


def P(t, s="body"):
    return Paragraph(t, S[s])


def code_block(lines, cjk=False):
    body = "<br/>".join(l.replace("&", "&amp;").replace("<", "&lt;").replace(" ", "&nbsp;")
                        for l in lines)
    t = Table([[Paragraph(body, S["codecjk"] if cjk else S["code"])]], colWidths=[165 * mm])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), CODEBG),
        ("BOX", (0, 0), (-1, -1), 0.5, RULE),
        ("LEFTPADDING", (0, 0), (-1, -1), 9), ("RIGHTPADDING", (0, 0), (-1, -1), 9),
        ("TOPPADDING", (0, 0), (-1, -1), 8), ("BOTTOMPADDING", (0, 0), (-1, -1), 8),
    ]))
    return t


def callout(title, text):
    inner = [[Paragraph("<b>%s</b>" % title, S["cellb"])], [Paragraph(text, S["note"])]]
    t = Table(inner, colWidths=[160 * mm])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), WARNBG),
        ("LINEBEFORE", (0, 0), (0, -1), 2.5, WARNEDGE),
        ("LEFTPADDING", (0, 0), (-1, -1), 10), ("RIGHTPADDING", (0, 0), (-1, -1), 10),
        ("TOPPADDING", (0, 0), (0, 0), 8), ("BOTTOMPADDING", (0, -1), (-1, -1), 8),
        ("TOPPADDING", (0, 1), (-1, -1), 2),
    ]))
    return KeepTogether(t)


def grid(rows, widths, mono_cols=(), align_right=()):
    data = []
    for r, row in enumerate(rows):
        out = []
        for c, cell in enumerate(row):
            if r == 0:
                out.append(Paragraph(str(cell), S["head"]))
            else:
                out.append(Paragraph(str(cell), S["mono"] if c in mono_cols else S["cell"]))
        data.append(out)

    t = Table(data, colWidths=widths, repeatRows=1)
    style = [
        ("BACKGROUND", (0, 0), (-1, 0), ACCENT),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("GRID", (0, 0), (-1, -1), 0.4, RULE),
        ("LINEBELOW", (0, 0), (-1, 0), 0.8, ACCENT),
        ("LEFTPADDING", (0, 0), (-1, -1), 6), ("RIGHTPADDING", (0, 0), (-1, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 5), ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
    ]
    for i in range(1, len(data)):
        if i % 2 == 0:
            style.append(("BACKGROUND", (0, i), (-1, i), BAND))
    for c in align_right:
        style.append(("ALIGN", (c, 1), (c, -1), "RIGHT"))
    t.setStyle(TableStyle(style))
    return t


def rule(space_before=13, space_after=9):
    t = Table([[""]], colWidths=[165 * mm], rowHeights=[0.1])
    t.setStyle(TableStyle([("LINEABOVE", (0, 0), (-1, 0), 0.8, RULE)]))
    return [Spacer(1, space_before), t, Spacer(1, space_after)]


# --------------------------------------------------------------------------- pages

def decorate(canvas, doc):
    canvas.saveState()
    if doc.page > 1:
        canvas.setFont("CJK", 7.8)
        canvas.setFillColor(MUTED)
        canvas.drawString(22 * mm, 13 * mm, "CompositeBody — Unity → TouchDesigner OSC 介接手冊")
        canvas.drawRightString(188 * mm, 13 * mm, "第 %d 頁" % doc.page)
        canvas.setStrokeColor(RULE)
        canvas.setLineWidth(0.4)
        canvas.line(22 * mm, 16 * mm, 188 * mm, 16 * mm)
    canvas.restoreState()


story = []

# ---- cover
story.append(Spacer(1, 42 * mm))
story.append(P("CompositeBody", "title"))
story.append(Spacer(1, 2 * mm))
story.append(P("Unity → TouchDesigner<br/>OSC 介接手冊", "title"))
story.append(Spacer(1, 7 * mm))
cover_rule = Table([[""]], colWidths=[70 * mm], rowHeights=[0.1])
cover_rule.setStyle(TableStyle([("LINEABOVE", (0, 0), (-1, 0), 2, ACCENT)]))
story.append(cover_rule)
story.append(Spacer(1, 7 * mm))
story.append(P("給 TouchDesigner 端與 Unity 端雙方的介接規格、設定步驟與現場檢查清單。", "subtitle"))
story.append(Spacer(1, 30 * mm))

meta = [
    ["文件版本", "2026-11-01 快照"],
    ["資料來源", "TouchDesignerSender.cs / OscWriter.cs / ShowTimelineDriver.cs / Main.playable"],
    ["Unity 版本", "6000.5.5f1"],
    ["演出總長", "2509 秒（41:49），20 個場景"],
]
mt = Table([[Paragraph("<b>%s</b>" % k, S["cell"]), Paragraph(v, S["cell"])] for k, v in meta],
           colWidths=[32 * mm, 133 * mm])
mt.setStyle(TableStyle([
    ("VALIGN", (0, 0), (-1, -1), "TOP"),
    ("LINEBELOW", (0, 0), (-1, -2), 0.4, RULE),
    ("LINEABOVE", (0, 0), (-1, 0), 0.4, RULE),
    ("LINEBELOW", (0, -1), (-1, -1), 0.4, RULE),
    ("LEFTPADDING", (0, 0), (-1, -1), 0), ("RIGHTPADDING", (0, 0), (-1, -1), 6),
    ("TOPPADDING", (0, 0), (-1, -1), 6), ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
]))
story.append(mt)
story.append(Spacer(1, 10 * mm))
story.append(P("場景清單會隨著 main timeline 重剪而改變。附錄 C 說明怎麼重新產生這份清單。", "foot"))

story.append(PageBreak())

# ---- 1
story.append(P("1　一頁速查", "h1"))
story.append(P("整條線就是一件事：Unity 這端每秒 60 次，把「現在是第幾景、演出跑到第幾秒、兩位玩家的頭和手在哪裡」"
               "包成一個 OSC bundle，用 UDP 丟到 TouchDesigner 那台機器的某個 port。", "body"))

quick = [
    ["項目", "值", "備註"],
    ["傳輸", "UDP（單向，Unity → TD）", "沒有回傳通道；TD 不會送東西回 Unity"],
    ["格式", "OSC 1.0 bundle", "一個 bundle = 一個 datagram = 同一個瞬間"],
    ["預設位址", "127.0.0.1 : 10000", "現場要改成 TD 那台的 LAN IP，見第 3 章"],
    ["頻率", "60 Hz", "刻意不是頭顯的 90；TD cook 60，送更快只是浪費頻寬"],
    ["封包大小", "約 600 bytes", "上限 1472，不分片"],
    ["誰送", "host 那一台（PC A）", "戴頭顯的 client 不送，見 3.2 Host Only"],
    ["namespace", "/cb", "可改；改了全部 address 跟著變"],
]
story.append(grid(quick, [26 * mm, 52 * mm, 87 * mm]))

story.append(Spacer(1, 7))
story.append(callout(
    "兩邊要先講好的三件事",
    "① TD 那台的 IP 與 port（預設 10000）　② 防火牆是否放行 inbound UDP　"
    "③ 場景編號要在哪一天凍結 —— running order 目前還在剪，編號會跟著動。"))

story.extend(rule())

# ---- 2
story.append(P("2　給 TouchDesigner 端", "h1"))

story.append(P("2.1　要開什麼", "h2"))
story.append(P("數字走 <b>OSC In CHOP</b>，字串走 <b>OSC In DAT</b>。這兩個都要，因為 "
               "OSC In CHOP 只會把數字變成 channel，字串它不處理。", "body"))
story.append(code_block([
    "OSC In CHOP        Network Port: 10000      ← 絕大部分資料在這裡",
    "OSC In DAT         Network Port: 10000      ← 只有場景代碼字串",
], cjk=True))
story.append(Spacer(1, 5))
story.append(P("兩個 operator 可以聽同一個 port，TD 會各自取它看得懂的訊息。", "body"))

story.append(P("2.2　完整 address 表", "h2"))
addr = [
    ["Address", "型別", "送出頻率", "意義"],
    ["/cb/show/time", "f", "每個封包", "演出時間（秒）。卡在 gate 的時候會停住不動"],
    ["/cb/show/held", "f", "每個封包", "1 = 正卡在 gate 等玩家，0 = 正常前進"],
    ["/cb/clock/now", "f", "每個封包", "共用時鐘（秒）。<b>不會</b>因為 gate 停，適合拿來跑環境動態"],
    ["/cb/clock/shared", "f", "每個封包", "1 = 真的在跟另一台同步，0 = 單機 fallback"],
    ["/cb/beat/number", "f", "每個封包", "第幾景，從 1 起算。0 = 不在任何一景（開演前或跑完）"],
    ["/cb/beat/code", "s", "換景時連送 3 次", "場景代碼字串，例如 O-0、S2-3。<b>只進 DAT</b>"],
    ["/cb/a/present", "f", "每個封包", "1 = 玩家 A 在線上。不在也會送，值為 0"],
    ["/cb/a/head/pos", "fff", "每個封包", "A 的頭位置 x, y, z（公尺）"],
    ["/cb/a/head/rot", "fff", "每個封包", "A 的頭旋轉 x, y, z（度）"],
    ["/cb/a/hand/l", "fff", "每個封包", "A 的左手位置（公尺）"],
    ["/cb/a/hand/r", "fff", "每個封包", "A 的右手位置（公尺）"],
    ["/cb/b/…", "—", "同上", "玩家 B，欄位與 /cb/a/… 完全一樣"],
]
story.append(grid(addr, [38 * mm, 12 * mm, 27 * mm, 88 * mm], mono_cols=(0, 1)))
story.append(Spacer(1, 6))
story.append(P("玩家不在線的時候，pos / rot / hand 還是會送，值是 0,0,0 —— 刻意的：channel 一旦消失，"
               "CHOP 的形狀會在 cook 中途改變，下游 operator 處理得很糟。請讀 present 來判斷，不要靠 channel 有沒有出現。",
               "body"))

story.append(PageBreak())

story.append(P("2.3　座標系", "h2"))
story.append(P("送出來的已經是 <b>右手座標、公尺、舞台座標</b>，TD 端不需要再轉一次。", "body"))
coord = [
    ["項目", "說明"],
    ["單位", "公尺"],
    ["原點", "舞台中心。地板是 y = 0"],
    ["手性", "右手系（Unity 是左手系，送出前已經把 Z 反號）"],
    ["旋轉", "尤拉角，單位是度。X 和 Y 已反號，Z 不變"],
    ["可關閉", "Unity 端 Convert To Right Handed 可關掉，關掉就送 Unity 的原始數值"],
]
story.append(grid(coord, [26 * mm, 139 * mm]))
story.append(Spacer(1, 6))
story.append(callout(
    "手性請用已知姿勢對一次",
    "手性接反看起來就是「鏡像」，而鏡像看起來非常合理，很容易一路錯到現場才發現。"
    "建議開演前讓一個人站到舞台上某個明確位置（例如往 +X 走兩公尺），在 TD 裡確認數字往你預期的方向跑。"))

story.append(P("2.4　場景編號", "h2"))
story.append(P("<b>/cb/beat/number</b> 是第幾景（1 起算），<b>/cb/beat/code</b> 是該景的代碼字串。"
               "兩個都直接來自 Unity 主時間軸上的 Control track —— 也就是說，"
               "導演在 timeline 上加一景、刪一景、換順序，這兩個值會自動跟著變，Unity 端不用改程式。", "body"))
story.append(Spacer(1, 2))
story.append(callout(
    "編號不是固定的合約",
    "正因為它跟著 timeline 走，重剪就會位移。2026-10-30 當時第 4 景是 S1-1，2026-11-01 已經變成第 4 景 S1-1、"
    "第 5 景 S1-3（S1-2 被拿掉、O-3 被併掉）。<br/><br/>"
    "<b>建議 TD 端以 /cb/beat/code 的字串為主</b>（例如比對 \"S2-3\"），number 只拿來做進度與排序。"
    "字串在重剪後至少還指著同一個場景名字。真的要用 number 綁死，請先跟 Unity 端約好凍結日。"))

story.append(P("2.5　收不到東西的時候", "h2"))
tshoot = [
    ["症狀", "先查這裡"],
    ["完全沒有封包", "①&nbsp;Unity 那台的 Host 是不是還指著 127.0.0.1　②&nbsp;TD 這台防火牆有沒有放行 inbound UDP 10000"],
    ["CHOP 有資料、DAT 沒有", "正常。code 只在換景的那一瞬間送 3 次，不換景就不會有新的列"],
    ["DAT 每次換景多出 3 列", "正常，而且是故意的。UDP 會掉包，連送 3 次是保險。3 列內容一樣，取最後一列即可"],
    ["number 一直是 0", "演出還沒開始，或已經跑完。不是錯誤"],
    ["present 一直是 0", "那位玩家的頭顯還沒進 session。pos 會是 0,0,0"],
    ["數字會動但位置是鏡像的", "手性。見 2.3"],
]
story.append(grid(tshoot, [40 * mm, 125 * mm]))

story.append(PageBreak())

# ---- 3
story.append(P("3　給 Unity 端（自己這邊的設定）", "h1"))

story.append(P("3.1　元件在哪", "h2"))
story.append(code_block([
    "MainScene.unity",
    "  └ Experience",
    "      └ ExperienceDirector",
    "          └ ShowTimeline          ← 選這個物件",
    "              └ Inspector：TouchDesigner Sender",
], cjk=True))
story.append(Spacer(1, 5))
story.append(P("同一個物件上還有 ShowTimelineDriver（演出時鐘）。放在一起是故意的：串流送的就是這個時鐘，"
               "兩者對不上的時候只有一個地方要看。", "body"))

story.append(P("3.2　Inspector 欄位", "h2"))
fields = [
    ["欄位", "預設", "說明"],
    ["Host", "127.0.0.1", "TD 那台的 IP 或電腦名稱。現場一定要改，見 3.3"],
    ["Port", "10000", "要跟 TD 的 OSC In CHOP 一致"],
    ["Namespace", "/cb", "所有 address 的前綴。改了等於改 TD 端的 channel 名稱"],
    ["Rate", "60", "每秒封包數"],
    ["Send Hands", "開", "關掉就不送手的位置，封包變小"],
    ["Send Beat", "開", "送場景編號"],
    ["Send Beat Code", "開", "送場景代碼字串。TD 端若只要數字可以關掉"],
    ["Beat Code Repeats", "3", "換景後連送幾個封包的代碼（1–10）"],
    ["Host Only", "開", "只有 host 送。關掉會變成兩台都送，TD 會收到兩條互相矛盾的流"],
    ["Convert To Right Handed", "開", "見 2.3"],
    ["Log First Packet", "開", "送出第一個封包時在 Console 寫一行，之後安靜"],
]
story.append(grid(fields, [40 * mm, 20 * mm, 105 * mm]))

story.append(P("3.3　設定 TD 的 IP：三種方式", "h2"))
story.append(P("三個來源同時存在，優先序由高到低如下。愈晚、愈現場的設定愈優先。", "body"))
prio = [
    ["優先序", "方式", "怎麼用", "撐過重啟"],
    ["1（最高）", "Staff panel", "演出中直接在桌面面板的 TouchDesigner 區輸入，按 Set TD Host。"
                                 "會重開 socket，<b>不用重啟</b>", "否"],
    ["2", "命令列參數", "啟動時加 <b>-tdhost 192.168.x.x</b>。build 沒有 Inspector，"
                        "這是現場不重新 build 就能改的方式", "是"],
    ["3（最低）", "Inspector 欄位", "Edit mode 改 Host 欄位然後存檔（Ctrl+S）", "是"],
]
story.append(grid(prio, [20 * mm, 26 * mm, 101 * mm, 18 * mm]))
story.append(Spacer(1, 6))
story.append(callout(
    "在 Editor 裡改 Host 的兩個陷阱",
    "① <b>要在 Edit mode 改並存檔。</b>Play mode 裡改的值，退出 Play 就被 Unity 還原。<br/>"
    "② <b>Play 中改 Host 不會生效。</b>socket 只在 OnEnable 開；已經在 Play 才拿到 IP 的話，"
    "把 component 的勾勾關掉再打開，或改用 staff panel 那一欄。"))

story.append(P("3.4　進場檢查清單", "h2"))
check = [
    ["#", "動作", "怎麼算過"],
    ["1", "跟 TD 端要 IP：在那台跑 ipconfig，取 LAN 介面的 IPv4", "拿到位址，且跟 Unity 這台同網段"],
    ["2", "TD 那台防火牆放行 inbound UDP 10000", "之前都跑 127.0.0.1，這條路徑從來沒測過"],
    ["3", "填入 Host（三種方式擇一，見 3.3）", "—"],
    ["4", "不進 Play 直接測一發：Inspector 上 TouchDesigner Sender 的標題列按右鍵 → "
          "<b>Send One Test Packet</b>", "Console 出現 [TouchDesigner] streaming to …"],
    ["5", "TD 端確認 OSC In CHOP 出現 channel", "看得到 show/time 之類在跳"],
    ["6", "進 Play，用已知姿勢對一次手性（見 2.3）", "往 +X 走，TD 的 X 跟著增加"],
    ["7", "跑一次換景，確認 DAT 收到 code", "換景時 DAT 多出 3 列一樣的代碼"],
]
story.append(grid(check, [10 * mm, 85 * mm, 70 * mm]))

story.append(P("3.5　網路", "h2"))
net = [
    ["項目", "說明"],
    ["防火牆", "需要在 TD 那台放行 inbound UDP（預設 10000）。127.0.0.1 不經過防火牆，所以舊設定測不到這一關"],
    ["同網段", "兩台要在同一個子網。VPN 或虛擬網卡會搶路由"],
    ["DHCP", "IP 每天會變的話，現場要固定 IP 或 DHCP reservation；或在 Host 欄位直接填電腦名稱"],
    ["名稱解析", "填電腦名稱的話，OnEnable 會做一次 DNS 解析。平面 switch 沒有 DNS 就不能用這招"],
]
story.append(grid(net, [24 * mm, 141 * mm]))

story.extend(rule())

# ---- appendix
story.append(P("附錄 A　封包長什麼樣子", "h1"))
story.append(P("一個 bundle，兩則訊息。中括號是不可見位元組（長度前綴、null 補齊、big-endian float）。", "body"))
story.append(code_block([
    "#bundle.............../cb/beat/number.,f..@......./cb/beat/code...,s..S0-1....",
    "|______||___________||_____________||__||______||____________||__||________|",
    "  header   time tag      address     tag  float     address    tag   string",
]))
story.append(Spacer(1, 5))
story.append(P("時間戳一律是 OSC 規範的「立即播放」。完整封包（換景那一刻、兩位玩家都在、手也開）實測 "
               "<b>592 / 1472 bytes</b>，離 MTU 還很遠。", "body"))

story.append(P("附錄 B　目前的 running order", "h1"))
story.append(P("2026-11-01 從 Main.playable 的 Control track 實際讀出。總長 2509 秒。", "body"))

scenes = [
    (1, "O-0", "進入", 0, 140), (2, "O-1", "鬼的抓取", 140, 310),
    (3, "O-2", "鬼魂交流", 310, 400), (4, "S1-1", "窗內的世界", 400, 570),
    (5, "S1-3", "黑白房子群", 570, 606), (6, "S1-4", "紅色剖開房子", 606, 649),
    (7, "S1-5", "霧氣之房左", 649, 671), (8, "S1-6", "霧氣之房右", 671, 709),
    (9, "S2-1", "門關上之後", 709, 759), (10, "S2-2", "碎片：燈", 759, 939),
    (11, "S2-3", "碎片：杯子", 939, 1059), (12, "S2-4", "隱藏的記憶", 1059, 1239),
    (13, "S3A-1", "把家道別", 1239, 1279), (14, "S3A-2", "回到依附的人", 1279, 1579),
    (15, "S3A-3", "物件化膜", 1579, 1669), (16, "S3A-4", "沒有說出口的話", 1669, 1909),
    (17, "S3B-1", "真人消失", 1909, 1969), (18, "S3B-2", "一片片的記憶", 1969, 2209),
    (19, "S3B-3", "最後一次旋轉", 2209, 2389), (20, "S3B-4", "膜鬆開", 2389, 2509),
]


def mmss(s):
    return "%d:%02d" % (s // 60, s % 60)


rows = [["number", "code", "場景名稱", "起", "迄", "長度"]]
for n, c, name, a, b in scenes:
    rows.append([str(n), c, name, mmss(a), mmss(b), "%ds" % (b - a)])
story.append(grid(rows, [18 * mm, 22 * mm, 65 * mm, 20 * mm, 20 * mm, 20 * mm],
                  mono_cols=(0, 1), align_right=(0, 3, 4, 5)))
story.append(Spacer(1, 6))
story.append(P("S1-2 不在清單上是 timeline 目前就沒有這一景；編號是數出來的，不會因此留空號。", "foot"))

story.append(Spacer(1, 9))
story.append(P("<b>Gate（演出會停下來等玩家的地方）</b>", "body"))
story.append(P("目前只有一處：第 301 秒，等 <font face='Mono'>beat.O1_Control</font>。"
               "沒有玩家完成互動的話演出會停在那裡，要靠 debug 鍵或 staff 放行。"
               "停住的時候 <font face='Mono'>/cb/show/held</font> 會變成 1，而 "
               "<font face='Mono'>/cb/clock/now</font> 仍然繼續走。", "body"))

story.append(P("附錄 C　怎麼重新產生附錄 B", "h1"))
story.append(P("場景清單只要 timeline 一改就過期。重新產生的方法是直接讀 asset，不要用手抄：", "body"))
story.append(code_block([
    "# Unity editor 開著的時候：",
    "unity cmd eval_file <script>.cs --result-only --json",
    "",
    "# script 內容：載入 Assets/_Timeline/Main.playable，",
    "# 取所有 ControlTrack 的 clip，依 clip.start 排序，",
    "# 場景代碼 = track 名稱第一個空格前的字串。",
], cjk=True))
story.append(Spacer(1, 5))
story.append(P("Unity 端在 runtime 用的是同一套規則（ShowTimelineDriver.CollectScenes），"
               "所以這樣讀出來的編號，跟實際送到 TD 的 /cb/beat/number 一致。", "body"))

story.append(Spacer(1, 10))
story.append(P("—— 手冊結束。規格有疑問時，程式碼註解是最終依據："
               "Assets/Scripts/Experience/TouchDesignerSender.cs 與 OscWriter.cs。", "foot"))


doc = BaseDocTemplate(OUT, pagesize=A4,
                      leftMargin=22 * mm, rightMargin=22 * mm,
                      topMargin=20 * mm, bottomMargin=22 * mm,
                      title="CompositeBody OSC 介接手冊",
                      author="CompositeBody")
frame = Frame(doc.leftMargin, doc.bottomMargin,
              doc.width, doc.height, id="body",
              leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
doc.addPageTemplates([PageTemplate(id="main", frames=[frame], onPage=decorate)])
doc.build(story)

print("written:", OUT, os.path.getsize(OUT), "bytes")
