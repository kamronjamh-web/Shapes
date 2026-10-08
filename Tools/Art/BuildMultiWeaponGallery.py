"""Build an offline gallery from the existing Blender review renders (no image edits)."""
import html
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'Art/WeaponConcepts'
OUT = ART / 'MultiWeaponCollection'
NEW = {'tesseract', 'hyperphere', 'pentachoron', 'hypercone',
       'impossiblecube', 'mobius', 'klein', 'boys'}
catalog = (ROOT / 'Scripts/Equipment/Data/EquipmentCatalogDefaults.cs').read_text(encoding='utf-8')
shapes = re.findall(r'CreateShape\("([^"]+)", "([^"]+)", ShapeGroup\.\w+, [\d.]+f, (\d),', catalog)
records = {}
for path in ART.glob('*/model-info.json'):
    info = json.loads(path.read_text(encoding='utf-8-sig'))
    key = info.get('equipped_shape')
    if key:
        records[key] = (path.parent, info)
assert len(shapes) == 16 and all(key in records for key, _, _ in shapes)
cards = []
manifest = []
for key, name, stars in shapes:
    folder, info = records[key]
    views = []
    for prefix, label in [('01', 'Three-quarter'), ('02', 'Side'), ('03', 'Shape dock'), ('04', 'Mechanism')]:
        files = sorted((folder / 'Renders').glob(prefix + '*.png'))
        if files:
            views.append((f'../{folder.name}/Renders/{files[0].name}', label))
    assert views, key + ' has no review renders'
    source = folder / (folder.name + '.blend')
    assert source.is_file()
    source_link = f'../{folder.name}/{source.name}'
    badge = 'Redesigned form' if key in NEW else 'Existing reference'
    title = info.get('title', name + ' - Handgun')
    buttons = ''.join(f'<button type="button" class="view" data-src="{html.escape(url)}" aria-pressed="{str(i == 0).lower()}">{label}</button>'
                      for i, (url, label) in enumerate(views))
    cards.append(f'''<article data-stars="{stars}" data-new="{str(key in NEW).lower()}">
      <a class="photo" href="{views[0][0]}" target="_blank" rel="noopener"><img loading="lazy" src="{views[0][0]}" alt="{html.escape(title)} — three-quarter view" width="1440" height="1080"></a>
      <div class="card-body"><div class="eyebrow"><span aria-label="{stars} stars">{'★' * int(stars)}</span><span>{badge}</span></div>
      <h2>{html.escape(title)}</h2><p class="description">{html.escape(info.get('design_name', 'Original MultiWeapon family'))}</p><div class="views" aria-label="Review angle">{buttons}</div>
      <a class="source" href="{source_link}">Open Blender model ↗</a></div></article>''')
    manifest.append({'shape': key, 'stars': int(stars), 'new_model': key in NEW,
                     'full_body_redesign': info.get('full_body_redesign', False),
                     'design_name': info.get('design_name', title),
                     'source': str(source.relative_to(ROOT)).replace('\\', '/'),
                     'preview': views[0][0], 'rarity_level_equipment_cases': info.get('rarity_level_equipment_cases'),
                     'source_reopened': info.get('blend_reopen_checked'),
                     'saved_animation_evaluated': info.get('saved_animation_evaluated')})

document = '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>MultiWeapon · Shape collection</title><style>
:root{color-scheme:dark;font:16px/1.55 system-ui,sans-serif;background:#10151b;color:#e3edf3}*{box-sizing:border-box}
body{max-width:1700px;margin:auto;padding:38px 32px 60px}header{max-width:950px;margin-bottom:28px}
.kicker{color:#8fc8f5;letter-spacing:.14em;text-transform:uppercase;font-size:12px;font-weight:700}
h1{font-size:clamp(30px,4vw,52px);letter-spacing:-.04em;margin:8px 0 12px;line-height:1.12}header p{color:#acbac8;max-width:760px}
.tiers{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin:26px 0}.tier{border-left:2px solid #507692;padding:0 14px;font-size:14px;color:#acbac8}.tier strong{display:block;color:#e5eef5}
.toolbar{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:24px 0}.toolbar button,.view{background:#1b2630;border:1px solid #384856;color:#c9d8e5;border-radius:6px;padding:8px 13px;cursor:pointer;font:inherit}
button[aria-pressed=true]{background:#bce0fa;color:#16232d;border-color:#bce0fa}button:focus-visible,a:focus-visible{outline:3px solid #81c6ff;outline-offset:3px}
.toolbar label{margin-left:8px;color:#b4c4d1}#count{margin-left:auto;color:#839baa;font-size:14px}.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:20px}
article{overflow:hidden;border:1px solid #35404b;border-radius:10px;background:#172029}article[hidden]{display:none}
.photo{display:block;background:#2b343d}.photo img{display:block;width:100%;height:auto;aspect-ratio:4/3;object-fit:contain}
.card-body{padding:18px}.eyebrow{display:flex;justify-content:space-between;gap:12px;color:#8db4d2;font-size:12px}.eyebrow span:first-child{letter-spacing:.15em;color:#e9d39b}
h2{font-size:19px;margin:9px 0 16px;line-height:1.35}.views{display:flex;flex-wrap:wrap;gap:6px}.view{font-size:12px;padding:6px 9px}.source{display:inline-block;margin-top:17px;color:#a5d7fb;text-decoration:none;font-size:14px}
.description{font-size:13px;color:#a9bac9;min-height:2.5em;margin:0 0 16px}
.comparisons{display:flex;gap:20px;flex-wrap:wrap}.comparisons a{color:#a5d7fb;font-size:14px;text-decoration:none}.comparisons a:hover{text-decoration:underline}
.source:hover{text-decoration:underline}footer{margin-top:32px;color:#8295a4;font-size:13px}#empty{padding:32px;border:1px solid #35404b;border-radius:8px;color:#bac9d4}
@media(max-width:1000px){.grid{grid-template-columns:repeat(2,1fr)}.tiers{grid-template-columns:repeat(2,1fr)}}
@media(max-width:640px){body{padding:24px 16px}.grid{grid-template-columns:1fr}.toolbar{gap:8px}#count{width:100%;margin:0}}
</style><header><div class="kicker">Shapes · MultiWeapon</div><h1>One tool. Sixteen forms.</h1>
<p>Eight redesigned 3- and 4-star forms: specialized machines, wearable tools and floating artifacts. Beveled metal, charcoal insets, shape cartridges and modulator bays carry the shared MultiWeapon identity.</p>
<div class="comparisons"><a href="../MultiWeaponReview_Tesseract_Hyperphere_Pentachoron_Hypercone/MultiWeapon_Lineup.png">View 3-star lineup</a><a href="../MultiWeaponReview_Impossiblecube_Mobius_Klein_Boys/MultiWeapon_Lineup.png">View 4-star lineup</a></div></header>
<div class="tiers"><div class="tier"><strong>★ · 1 star</strong>Direct mechanisms and clean armor.</div>
<div class="tier"><strong>★★ · 2 stars</strong>Dimensional housings and articulated emitters.</div>
<div class="tier"><strong>★★★ · 3 stars</strong>Rift frame, shoulder mortar, lightning gauntlet and drill lance.</div>
<div class="tier"><strong>★★★★ · 4 stars</strong>Fractured cube, forearm ribbon, aura vessel and clone sculpture.</div></div>
<nav class="toolbar" aria-label="Filter weapons"><button data-tier="all" aria-pressed="true">All tiers</button>
<button data-tier="1" aria-pressed="false">1 star</button><button data-tier="2" aria-pressed="false">2 stars</button>
<button data-tier="3" aria-pressed="false">3 stars</button><button data-tier="4" aria-pressed="false">4 stars</button>
<label><input id="new-only" type="checkbox" checked> Redesigned forms only</label><span id="count" aria-live="polite"></span></nav>
<main class="grid">''' + '\n'.join(cards) + '''</main>
<p id="empty" hidden>Uncheck “Redesigned forms only” to compare the existing 1- and 2-star models.</p>
<footer>Stars belong to the shape family. Common–Divine item rarity controls accent color separately; these review renders use Rare blue.
<br>All sixteen forms are available in the game. This gallery links their editable Blender sources and static review renders.</footer>
<script>
let tier='all';const cards=[...document.querySelectorAll('article')],only=document.querySelector('#new-only');
function filter(){let count=0;for(const card of cards){card.hidden=(tier!=='all'&&card.dataset.stars!==tier)||(only.checked&&card.dataset.new!=='true');if(!card.hidden)count++}document.querySelector('#count').textContent=count+' models';document.querySelector('#empty').hidden=count>0}
document.querySelectorAll('[data-tier]').forEach(button=>button.addEventListener('click',()=>{tier=button.dataset.tier;document.querySelectorAll('[data-tier]').forEach(other=>other.setAttribute('aria-pressed',String(other===button)));filter()}));
only.addEventListener('change',filter);
document.querySelectorAll('.view').forEach(button=>button.addEventListener('click',()=>{const card=button.closest('article');card.querySelector('img').src=button.dataset.src;card.querySelector('img').alt=card.querySelector('h2').textContent+' — '+button.textContent;card.querySelector('.photo').href=button.dataset.src;card.querySelectorAll('.view').forEach(other=>other.setAttribute('aria-pressed',String(other===button)))}));filter();
</script></html>'''

OUT.mkdir(parents=True, exist_ok=True)
(OUT / '.gdignore').write_text('', encoding='utf-8')
(OUT / 'Weapons.html').write_text(document, encoding='utf-8')
(OUT / 'collection.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(f'Gallery: {OUT / "Weapons.html"}; {len(manifest)} shapes, {len(NEW)} new models')
