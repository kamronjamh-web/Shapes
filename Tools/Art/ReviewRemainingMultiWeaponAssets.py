"""Review redesigned star tiers and refresh every affected comparison board."""
import json
from pathlib import Path
import sys
import traceback

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(Path(__file__).parent))
sys.dont_write_bytecode=True
import ReviewMultiWeaponAssets as review

GROUPS=[['tesseract','hyperphere','pentachoron','hypercone'],
        ['impossiblecube','mobius','klein','boys'],
        ['cone','tesseract','hyperphere'],
        ['pentachoron','hypercone','impossiblecube'],
        ['mobius','klein','boys'],
        ['square','cone','hyperphere','klein']]
STATUS=ROOT/'.tmp/multiweapon-remaining-review-status.json'


def main():
    reports=[]
    for group in GROUPS:
        STATUS.write_text(json.dumps({'stage':'reviewing','shapes':group},indent=2),encoding='utf-8')
        sys.argv=[__file__,'--','--weapons',*group]
        review.main()
        folder=ROOT/'Art/WeaponConcepts'/('MultiWeaponReview_'+'_'.join(key.title() for key in group))
        reports.append({'shapes':group,'folder':str(folder.relative_to(ROOT)).replace('\\','/'),
                        'validation':json.loads((folder/'validation.json').read_text(encoding='utf-8'))})
    STATUS.write_text(json.dumps({'stage':'complete','reviews':reports},indent=2),encoding='utf-8')
    print('REMAINING_REVIEW_COMPLETE',flush=True)


if __name__=='__main__':
    STATUS.parent.mkdir(parents=True,exist_ok=True)
    sys.stdout=open(ROOT/'.tmp/multiweapon-remaining-review.log','w',encoding='utf-8',buffering=1)
    sys.stderr=sys.stdout
    try:
        main()
    except Exception as error:
        traceback.print_exc()
        STATUS.write_text(json.dumps({'stage':'failed','error':str(error)},indent=2),encoding='utf-8')
        raise
