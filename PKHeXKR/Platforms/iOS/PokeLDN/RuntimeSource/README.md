# iOS PokeLDN 런타임 (호스트 파일만)

PokeLDN 파이썬 코드(pokeldn, ldn, bin, kr_trade.py 등)는 여기 두지 않습니다. `build_ios_runtime.py`가 빌드할 때
안드로이드 LDN 라이브러리 `Platforms/Android/ldn/pokeldn-trade-kr.aar`(`assets/chaquopy/app.imy`, `requirements-common.imy`)에서
꺼내 씁니다 → 안드로이드와 iOS가 늘 같은 코드로 교환합니다. 그 코드의 원본은 `ldnapp/src/python`(LDN 소스 묶음)입니다.

- `ios_host.py`: 네이티브 호스트(`native/PokeLdnRuntime.m`)가 부르는 start/poll/busy/cancel (안드로이드 LdnBridge와 같은 JSON)
- `ios_bootstrap.py`: pycryptodome 네이티브 모듈을 iOS 프레임워크(.fwork)에서 읽게 함
- 블루투스(CoreBluetooth) ↔ 127.0.0.1:47811 소켓 다리는 `native/PokeLdnRuntime.m`

PokeLDN: AGPL-3.0 (`LICENSE`), 원본 https://github.com/Decryptu/pokeldn
