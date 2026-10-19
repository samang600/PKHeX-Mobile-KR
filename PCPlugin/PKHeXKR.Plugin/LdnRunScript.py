# PKHeXKR 플러그인: PokeLDN 실행 + 교환 전 합법성 확인 건너뛰기 (PKHeX 판정이 불법이어도 경고만 하고 보냄)
import base64
import os
import runpy
import sys

if os.getcwd() not in sys.path:
    sys.path.insert(1, os.getcwd())

from pokeldn import pokemon
from pokeldn.app import runner


def prepare(self, game, data, *, fresh=False, fields=None):
    reply = self.check_bytes(game, data, fresh=fresh, fields=fields)
    if not reply.get("legal", True):
        lines = [l.strip() for l in str(reply.get("report") or "").splitlines() if l.strip()]
        print("[PKHeXKR] 합법성 확인 건너뜀 - PKHeX 판정은 불법" + (f" ({lines[0]})" if lines else ""), flush=True)
    if reply.get("note"):
        print(f"[pokemon] the offer {reply['note']}", flush=True)
    return base64.b64decode(reply["data"])


def inner(argv):
    # 바깥 실행기가 이미 입력 감시를 시작했으므로 도구만 실행 (runner.child의 실행 부분)
    mode, target, *args = argv
    if mode == "--run":
        path = os.path.join(runner.ROOT, target)
        sys.argv = [path, *args]
        sys.path.insert(0, os.path.dirname(path))
        runpy.run_path(path, run_name="__main__")
    else:
        sys.argv = [target, *args]
        runpy.run_module(target, run_name="__main__", alter_sys=True)


pokemon.Service.prepare = prepare
runner.child = inner

from pokeldn.__main__ import main

raise SystemExit(main(sys.argv[1:]))
