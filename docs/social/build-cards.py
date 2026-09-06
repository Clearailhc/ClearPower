#!/usr/bin/env python3
"""Render the six social cards (1080x1440 @2x) into this directory.

    pip install playwright && playwright install chromium
    CARD_FONTS=/path/to/noto-sans-sc python3 build-cards.py

CARD_FONTS points at a directory with a noto.css @font-face sheet and its woff2 files
(the Google Fonts CSS for Noto Sans SC, URLs rewritten to local paths)."""
import base64, pathlib
from playwright.sync_api import sync_playwright

HERE = pathlib.Path(__file__).parent          # docs/social
REPO = HERE.parent.parent
FONTS = pathlib.Path(__import__("os").environ.get("CARD_FONTS", HERE / "fonts")).resolve()  # a dir with noto.css + woff2
OUT = HERE

def b64(p): return base64.b64encode(pathlib.Path(p).read_bytes()).decode()
SHOT_LINUX = b64(REPO / "docs/popover.png")
SHOT_WIN = b64(REPO / "docs/popover-windows.png")
ICON = (REPO / "icons/org.clearpower.ClearPower.svg").read_text()

CSS = f"""
<link rel="stylesheet" href="file://{FONTS}/noto.css">
<style>
:root {{
  --bg:#17181c; --card:#22242a; --card2:#2a2d34; --line:#3a3e47;
  --ink:#f1f2f4; --ink2:#b6bac3; --ink3:#7f8591;
  --cpu:#8b7fd4; --gpu:#c77aa0; --soc:#6f9be6; --mem:#9aa3b2; --scr:#d9a441; --oth:#4fb8a8;
  --ac:#6f9be6; --bat:#4fc386; --accent:#4fc386;
}}
* {{ box-sizing:border-box; margin:0; padding:0 }}
html,body {{ width:1080px; height:1440px; background:var(--bg); color:var(--ink);
  font-family:'Noto Sans SC','WenQuanYi Zen Hei',sans-serif; -webkit-font-smoothing:antialiased }}
.page {{ width:1080px; height:1440px; padding:96px 76px 64px; display:flex; flex-direction:column; position:relative; overflow:hidden }}
.kicker {{ font-size:28px; color:var(--ink3); margin-bottom:22px }}
h1 {{ font-size:82px; line-height:1.2; font-weight:900; letter-spacing:-.01em }}
h1 .ac {{ color:var(--accent) }}
.sub {{ font-size:38px; color:var(--ink2); line-height:1.5; margin-top:26px; font-weight:500 }}
.grow {{ flex:1 }}
.foot {{ display:flex; justify-content:space-between; align-items:center; font-size:24px; color:var(--ink3); margin-top:36px }}
.foot b {{ color:var(--ink2); font-weight:700 }}
.pill {{ display:inline-flex; align-items:center; padding:12px 26px; border-radius:999px; background:var(--card); border:1.5px solid var(--line); font-size:27px; color:var(--ink2); font-weight:500 }}
.box {{ background:var(--card); border:1.5px solid var(--line); border-radius:28px; padding:34px 38px }}
.num {{ font-weight:900; letter-spacing:-.02em; line-height:1 }}
.item {{ display:flex; gap:26px; align-items:baseline; padding:26px 0; border-top:1.5px solid var(--line) }}
.item:first-child {{ border-top:0; padding-top:8px }}
.item:last-child {{ padding-bottom:8px }}
.item b {{ font-size:36px; font-weight:900; width:150px; flex:none }}
.item p {{ font-size:29px; color:var(--ink2); line-height:1.5 }}
.mono {{ font-family:'JetBrains Mono','DejaVu Sans Mono',monospace }}
.tag {{ font-size:24px; color:var(--ink3) }}
</style>
"""

def page(body, title, kicker=None, n=None):
    dots = "".join(f'<i style="display:inline-block;width:12px;height:12px;border-radius:50%;margin-left:10px;background:{"var(--ink2)" if i==n else "var(--line)"}"></i>' for i in range(1,7))
    return f"""<!doctype html><html lang="zh-CN"><head><meta charset="utf-8">{CSS}</head><body>
<div class="page">
  {f'<div class="kicker">{kicker}</div>' if kicker else ''}
  <h1>{title}</h1>
  {body}
  <div class="foot"><span><b>ClearPower</b></span><span>{dots}</span></div>
</div></body></html>"""

cards = {}

# 1 ── cover: battery health, the reason anyone installs this ──────────────
cards["1-cover"] = f"""<!doctype html><html lang="zh-CN"><head><meta charset="utf-8">{CSS}
<style>
.hero {{ display:flex; align-items:center; gap:18px; margin-bottom:44px }}
.hero svg {{ width:72px; height:72px }}
.hero .name {{ font-size:44px; font-weight:900 }}
.hero .oss {{ margin-left:auto; font-size:26px; color:var(--ink3) }}
h1 {{ font-size:88px }}
.shots {{ position:absolute; left:0; right:0; bottom:120px; height:580px }}
.shot {{ position:absolute; right:60px; bottom:0; width:430px; border-radius:26px; box-shadow:0 40px 90px rgba(0,0,0,.6); border:1.5px solid var(--line); transform:rotate(-2deg); overflow:hidden }}
.shot img {{ display:block; width:104%; margin:-2% }}
.shot2 {{ position:absolute; left:70px; bottom:40px; width:420px; border-radius:22px; box-shadow:0 30px 70px rgba(0,0,0,.6); border:1.5px solid var(--line); transform:rotate(3deg); overflow:hidden }}
.shot2 img {{ display:block; width:100% }}
</style></head><body><div class="page">
  <div class="hero">{ICON}<div class="name">ClearPower</div><div class="oss">开源项目</div></div>
  <h1>笔记本一直插着电，<br>电池老得最快。</h1>
  <div class="sub" style="color:var(--ink)">ClearPower 让它充到 <span style="color:var(--accent);font-weight:900">80%</span> 就停，Linux、macOS、Windows 都能用。</div>
  <div class="shots"><div class="shot2"><img src="data:image/png;base64,{SHOT_WIN}"></div><div class="shot"><img src="data:image/png;base64,{SHOT_LINUX}"></div></div>
  <div class="grow"></div>
  <div class="foot"><span>github.com/Clearailhc/ClearPower</span><span></span></div>
</div></body></html>"""

# 2 ── set it once, stop thinking about it ─────────────────────────────────
cards["2-charge"] = page(f"""
<div class="box" style="margin-top:44px;background:#1b1d22;padding:24px 30px">
  <div style="display:flex;align-items:center;gap:18px">
    <span style="background:#2d3038;border-radius:16px;padding:14px 26px;font-size:32px;font-weight:700">上限 80%</span>
    <span class="grow"></span>
    <span style="background:#2d3038;border-radius:16px;padding:14px 26px;font-size:32px;font-weight:700">放电 <span style="color:var(--ink2)">－</span></span>
    <span style="background:#2d3038;border-radius:16px;padding:14px 26px;font-size:32px;font-weight:700">补满 <span style="color:var(--ink2)">＋</span></span>
    <span style="background:#2d3038;border-radius:50%;width:66px;height:66px;display:flex;align-items:center;justify-content:center;font-size:30px">⚙</span>
  </div>
  <div style="margin-top:22px;height:56px;border-radius:16px;background:#2d3038;position:relative;overflow:hidden">
    <div style="position:absolute;left:0;top:0;bottom:0;width:80%;background:linear-gradient(90deg,#4aa3e8,#58b0f0);border-radius:16px"></div>
    <div style="position:absolute;left:24px;top:0;line-height:56px;font-size:28px;font-weight:700">80%</div>
    <div style="position:absolute;left:calc(80% - 2px);top:8px;bottom:8px;border-left:3px dashed rgba(255,255,255,.7)"></div>
  </div>
  <div style="margin-top:14px;font-size:26px;color:var(--ink2)">已到上限 · 外接供电</div>
</div>
<div class="box" style="padding:20px 38px;margin-top:28px">
  <div class="item"><b>上限</b><p>点一下在 80、90、100 之间切换，设置里 50 到 100 都可以选。</p></div>
  <div class="item"><b>补满</b><p>出门前点一下，充到 100%，回来自动恢复上限。</p></div>
  <div class="item"><b>放电</b><p>电池已经充到 95% 了？直接放回上限，不用等它自己慢慢掉。ThinkPad 和 Mac 支持。</p></div>
</div>
<div class="grow"></div>
""", "设好上限，<br>电池的事就不用再管了。", n=2)

# 3 ── battery life: the screen number, framed as something you can act on ─
def bar(label, pct, value):
    return f"""
  <div style="display:flex;align-items:center;gap:26px">
    <div style="width:220px;font-size:34px;font-weight:700">{label}</div>
    <div style="flex:1;height:72px;background:var(--card2);border-radius:16px;overflow:hidden"><div style="width:{pct}%;height:100%;background:var(--scr);border-radius:16px"></div></div>
    <div style="width:250px;text-align:right;font-size:92px;white-space:nowrap" class="num">{value} <span style="font-size:40px;color:var(--ink2);font-weight:700">W</span></div>
  </div>"""

cards["3-screen"] = page(f"""
<div style="margin-top:60px;display:flex;flex-direction:column;gap:30px">
  {bar("白底网页", 100, "7")}
  {bar("深色桌面", 5.7, "0.4")}
</div>
<div class="sub" style="margin-top:70px">同一块屏、同样的亮度，差别只在画面是白底还是深色。这个数系统里看不到，ClearPower 把屏幕单独算了出来，校准一次就行。</div>
<div class="grow"></div>
""", "<span style=\"font-size:74px\">换个深色主题，<br>屏幕功耗从 7 W 降到 <span style=\"white-space:nowrap\">0.4 W</span>。</span>", kicker="OLED 笔记本想多用一会儿？先看屏幕", n=3)

# 4 ── seeing where the power goes ─────────────────────────────────────────
def sankey():
    sinks = [("cpu","CPU","2.5 W"),("gpu","GPU","0.2 W"),("soc","SoC","1.8 W"),
             ("mem","内存","0.5 W"),("scr","屏幕","≈1.0 W"),("oth","其他","2.0 W")]
    vals = [2.5,0.2,1.8,0.5,1.0,2.0]; total = 8.0
    hub_y0, hub_h = 130, 400
    out = []
    src = [("ac","适配器","5.0 W",5.0,50),("bat","电池","3.0 W",3.0,360)]
    y = hub_y0
    for key,name,w,v,sy in src:
        h = hub_h*v/total; sh = 150
        out.append(f'<path d="M150,{sy} C240,{sy} 240,{y} 330,{y} L330,{y+h} C240,{y+h} 240,{sy+sh} 150,{sy+sh} Z" fill="url(#g-{key})" opacity=".85"/>')
        out.append(f'<rect x="10" y="{sy}" width="140" height="{sh}" rx="22" fill="var(--card2)" stroke="var(--{key})" stroke-width="3"/>')
        out.append(f'<text x="80" y="{sy+62}" text-anchor="middle" font-size="26" fill="var(--ink2)" font-family="Noto Sans SC">{name}</text>')
        out.append(f'<text x="80" y="{sy+108}" text-anchor="middle" font-size="34" font-weight="900" fill="var(--ink)" font-family="Noto Sans SC">{w}</text>')
        y += h
    out.append(f'<rect x="330" y="{hub_y0}" width="140" height="{hub_h}" rx="24" fill="var(--card2)" stroke="#6b7280" stroke-width="3"/>')
    out.append(f'<text x="400" y="{hub_y0+hub_h/2-16}" text-anchor="middle" font-size="26" fill="var(--ink2)" font-family="Noto Sans SC">整机</text>')
    out.append(f'<text x="400" y="{hub_y0+hub_h/2+30}" text-anchor="middle" font-size="36" font-weight="900" fill="var(--ink)" font-family="Noto Sans SC">8.0 W</text>')
    y = hub_y0; sy = 10; sh = 76
    for (key,name,w),v in zip(sinks,vals):
        h = hub_h*v/total
        out.append(f'<path d="M470,{y} C600,{y} 600,{sy} 700,{sy} L700,{sy+sh} C600,{sy+sh} 600,{y+h} 470,{y+h} Z" fill="url(#g-{key})" opacity=".85"/>')
        out.append(f'<rect x="700" y="{sy}" width="218" height="{sh}" rx="20" fill="var(--card2)" stroke="var(--{key})" stroke-width="3"/>')
        out.append(f'<text x="724" y="{sy+48}" font-size="28" fill="var(--ink2)" font-family="Noto Sans SC">{name}</text>')
        out.append(f'<text x="896" y="{sy+48}" text-anchor="end" font-size="30" font-weight="900" fill="var(--ink)" font-family="Noto Sans SC">{w}</text>')
        y += h; sy += sh + 22
    defs = "".join(f'<linearGradient id="g-{k}" x1="0" x2="1"><stop offset="0" stop-color="var(--{a})"/><stop offset="1" stop-color="var(--{b})"/></linearGradient>'
                   for k,a,b in [("ac","ac","mem"),("bat","bat","mem"),("cpu","mem","cpu"),("gpu","mem","gpu"),("soc","mem","soc"),("mem","mem","mem"),("scr","mem","scr"),("oth","mem","oth")])
    return f'<svg viewBox="0 0 928 600" width="100%" style="display:block"><defs>{defs}</defs>{"".join(out)}</svg>'

cards["4-flow"] = page(f"""
<div style="margin-top:40px">{sankey()}</div>
<div class="sub" style="margin-top:30px">CPU、显卡、内存、屏幕各吃多少一目了然。数字都是传感器测出来的，六项加起来正好是整机的 8 W，没有估的。</div>
<div class="grow"></div>
""", "每一瓦去了哪，<br>一张图就看清了。", n=4)

# 5 ── runtime estimate, and staying out of the way ────────────────────────
cards["5-runtime"] = page(f"""
<div class="box" style="margin-top:44px;padding:20px 38px">
  <div class="item"><b>续航</b><p>按电池最近 10 分钟、30 分钟、1 小时的实际耗电算，不会一会儿显示 2 小时、一会儿 6 小时。</p></div>
  <div class="item"><b>状态</b><p>温度、风扇、电源模式、哪个应用在耗电，都在同一个弹窗里。</p></div>
  <div class="item"><b>后台</b><p>弹窗关着的时候降低采样频率，也不画图，自己不当耗电大户。</p></div>
</div>
<div class="grow"></div>
""", "还能用多久算得稳，<br>平时也不碍事。", n=5)

# 6 ── three platforms, one open-source project ────────────────────────────
def f(name, tag): return f'<div class="box" style="padding:20px 30px;font-size:28px;display:flex;justify-content:space-between"><span class="mono">{name}</span><span class="tag">{tag}</span></div>'
cards["6-download"] = page(f"""
<div style="display:flex;flex-direction:column;gap:12px;margin-top:44px">
  {f("clearpower_0.5.1_all.deb","Linux · GNOME")}
  {f("ClearPower-0.5.1-arm64.dmg","macOS · Apple Silicon")}
  {f("ClearPower-Setup-0.5.1-x64.exe","Windows 11")}
  {f("ClearPower-0.5.1-x64-portable.zip","Windows · 免安装")}
</div>
<div class="tag" style="font-size:26px;line-height:1.7;margin-top:40px">功耗分解需要 Intel 或 Apple 芯片，充电上限目前支持 ThinkPad 和 Apple Silicon Mac。Apache-2.0，欢迎提 issue 和 PR。</div>
<div style="margin-top:36px;font-size:36px;font-weight:900;color:var(--accent)">github.com/Clearailhc/ClearPower</div>
<div class="grow"></div>
""", "三个平台，<br>同一个开源项目。", n=6)

import tempfile
TMP = pathlib.Path(tempfile.mkdtemp())
for name, html in cards.items():
    (TMP / f"{name}.html").write_text(html)

with sync_playwright() as p:
    b = p.chromium.launch(args=["--no-sandbox"])
    ctx = b.new_context(viewport={"width":1080,"height":1440}, device_scale_factor=2)
    pg = ctx.new_page()
    for name in cards:
        pg.goto(f"file://{TMP}/{name}.html"); pg.wait_for_timeout(600)
        pg.screenshot(path=str(OUT / f"{name}.png"))
        print("wrote", name)
    b.close()
