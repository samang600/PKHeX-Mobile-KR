# PKHeXKR 플러그인: PokeLDN 실행기.
# - 'pokeldn <도구>'는 데스크톱 앱 프리셋의 기본값을 그대로 붙이는데, 그중 'New PID each run'(--fresh-pid)이 기본으로 켜져 있어
#   보낸 포켓몬의 PID·암호화 상수가 새로 뽑힘 → 받은 포켓몬이 HOME에 들어가지 않음. 여기서 항상 뺌 (PID·EC는 절대 바꾸지 않음).
# - PKHEXKR_SKIP_LEGAL=1이면 교환 전 합법성 확인을 건너뜀 (PKHeX 판정이 불법이어도 경고만 하고 보냄)
import base64
import os
import runpy
import sys

if os.getcwd() not in sys.path:
    sys.path.insert(1, os.getcwd())

from pokeldn import pokemon
from pokeldn.app import command, runner

_build = command.build


def build_keep_pid(*args, **kwargs):
    out = _build(*args, **kwargs)
    if "--fresh-pid" in out:
        print("[PKHeXKR] 'New PID each run' 끔 - 보낸 포켓몬의 PID·암호화 상수를 그대로 둠", flush=True)
    return [a for a in out if a != "--fresh-pid"]


command.build = build_keep_pid


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


if os.environ.get("PKHEXKR_SKIP_LEGAL") == "1":
    pokemon.Service.prepare = prepare
runner.child = inner

from pokeldn.__main__ import main

raise SystemExit(main(sys.argv[1:]))
