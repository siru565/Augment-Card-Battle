# 개발용: 스크린샷 여러 장을 한 장으로 모아서 확인하기 쉽게 만듭니다.
import sys
from PIL import Image
names = sys.argv[2:] or ['01_main_menu', '02_settings', '03_augment_draft', '04_table', '11_ranking', '12_room']
cols = 3
w, h = 640, 360
rows = (len(names) + cols - 1) // cols
sheet = Image.new('RGB', (cols * w, rows * h))
for i, n in enumerate(names):
    im = Image.open(f'tools/shots/devlog/{n}.png').convert('RGB').resize((w, h))
    sheet.paste(im, ((i % cols) * w, (i // cols) * h))
sheet.save(sys.argv[1], quality=88)
