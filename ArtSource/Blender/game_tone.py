"""게임 톤 맞추기: 캐릭터·소품·배경 3종의 베이스컬러와 질감을 같은 값으로 보정한다.

VARCO 3D 텍스처는 리소스마다 채도·밝기·광택이 제각각이라, 세 리소스 모두 이 파일의 값 하나로 보정한다.
값을 바꾸면 각 리소스의 파이프라인을 다시 돌려 셋을 함께 갱신한다.
"""
import numpy as np

SATURATION = 0.9      # 채도: AI 텍스처의 과한 채도를 살짝 눌러 실사 코트와 어울리게
CONTRAST = 1.04       # 대비
WARMTH = 0.015        # 따뜻한 쪽으로 (R 올리고 B 내림), 야외 햇빛 느낌
BRIGHTNESS = 1.0
ROUGHNESS_MIN = 0.45  # 거칠기 하한: 천·피부가 플라스틱처럼 번들거리지 않게


def tone(rgb):
    """sRGB 0~1 값 배열 (..., 3)에 게임 톤을 입힌다."""
    lum = rgb @ np.array([0.299, 0.587, 0.114], dtype=rgb.dtype)
    rgb = lum[..., None] + (rgb - lum[..., None]) * SATURATION
    rgb = (rgb - 0.5) * CONTRAST + 0.5
    rgb[..., 0] += WARMTH
    rgb[..., 2] -= WARMTH
    return np.clip(rgb * BRIGHTNESS, 0.0, 1.0)


def smoothness(roughness):
    """glTF 거칠기(0~1)를 Unity URP 매끈함(smoothness)으로. 거칠기 하한을 먼저 적용한다."""
    return 1.0 - np.maximum(roughness, ROUGHNESS_MIN)
