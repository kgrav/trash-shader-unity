import os
from PIL import Image
import numpy as np

SUFFIX = '31'

folder_path = '.mesne'
# Load all images
images = []
for i in range(11, 101, (100 - 11) // 15):
    img_path = os.path.join(folder_path, f"00{i}.png")
    images.append(Image.open(img_path))
    print(f"Adding image: {img_path}")

# Combine into a single sprite sheet
width, height = images[0].size
sprite_sheet = Image.new("RGBA", (width * 16, height))

for idx, img in enumerate(images):
    sprite_sheet.paste(img, (idx * width, 0))

# Save the sprite sheet
os.makedirs('spritesheets', exist_ok=True)
filename = f'spritesheets/row_{SUFFIX}.png'
sprite_sheet.save(filename)
print(f"Sprite sheet created successfully and saved to '{filename}'")