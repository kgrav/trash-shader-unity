import os
from PIL import Image
import math

##        /////////////////////////////////
##       ///  SET THESE   ////////////////
##      /////////////////////////////////
BASE_PATH = r'c:/Users/richa/GitHub/trash-shader-unity/.artsource/.frames'
#CHROMA_QUALITY, ALPHA_QUALITY = 5, 0  # ROW 0
#CHROMA_QUALITY, ALPHA_QUALITY = 60, 2 # ROW 1
#CHROMA_QUALITY, ALPHA_QUALITY = 2, 80 # ROW 2
CHROMA_QUALITY, ALPHA_QUALITY = 90, 90 # ROW 3

##  /////////////////////////////////
## // Okay the rest will run fine //
##/////////////////////////////////


def process_frames(base_path):
    
    files = os.listdir(base_path)

    if not files:
        print(f"No files found in {base_path}.")
        return
    
    parent_folder = os.path.dirname(base_path)
    mesne_folder = os.path.join(parent_folder, ".mesne")
    os.makedirs(mesne_folder, exist_ok=True)

    for file_path in files:
        file_name = os.path.basename(file_path)
        #file_path = os.path.join(folder_path, file_name)
        print(f"Found file: {file_path}")
        if file_path.lower().endswith(('.png', '.jpg', '.jpeg')):
            print(f"Processing image: {file_path}")
            image_path = os.path.join(base_path, file_path)
            process_image(image_path, mesne_folder)


def cronch(image):

    try:
        # Extract RGB portion
        rgb_image = image.convert("RGB")
        
        # Save RGB portion as lowest quality JPEG
        rgb_output = Image.new("RGB", rgb_image.size)
        rgb_output.paste(rgb_image)
        rgb_output_path = "temp_rgb.jpg"
        rgb_output.save(rgb_output_path, "JPEG", quality=CHROMA_QUALITY)

        # Extract alpha channel and convert to monochromatic
        alpha_channel = image.getchannel("A")
        mono_alpha = alpha_channel.convert("L")
        
        # Save monochromatic alpha channel as lowest quality JPEG
        alpha_output = Image.new("L", mono_alpha.size)
        alpha_output.paste(mono_alpha)
        alpha_output_path = "temp_alpha.jpg"
        alpha_output.save(alpha_output_path, "JPEG", quality=ALPHA_QUALITY)

        return rgb_output_path, alpha_output_path

    except Exception as e:
        print(f"Error in cronch function: {e}")
        return None, None

def process_image(image_path, output_folder):
    
    try:
        with Image.open(image_path) as img:

            # Rotate the image 15 degrees anticlockwise about the center
            rotated_img = img.rotate(25, resample=Image.BICUBIC, expand=True)
            
            # Downsample to 64x64
            downsampled_img = rotated_img.resize((128, 128), Image.Resampling.NEAREST)
            
            # Translate the image 6 pixels to the left and 13 pixels upwards
            translated_img = downsampled_img.transform(
                downsampled_img.size,
                Image.AFFINE,
                (1, 0, 6*2, 0, 1, 13*2),
                resample=Image.NEAREST
            )

            cronched_rgb_path, cronched_alpha_path = cronch(translated_img)

            # Load the output images from cronched_rgb_path and cronched_alpha_path
            with Image.open(cronched_rgb_path) as cronched_rgb, Image.open(cronched_alpha_path) as cronched_alpha:
                # Convert to RGBA if needed
                if cronched_rgb.mode != "RGBA":
                    cronched_rgb = cronched_rgb.convert("RGBA")
                if cronched_alpha.mode != "L":
                    cronched_alpha = cronched_alpha.convert("L")

                # Overwrite the alpha channel of cronched_rgb with the value of cronched_alpha
                combined_image = cronched_rgb.copy()
                combined_image.putalpha(cronched_alpha)

                output_path = os.path.join(output_folder, os.path.basename(image_path))
                print(f"Saving processed image to: {output_path}")
                combined_image.save(output_path)




                return combined_image
        

    except Exception as e:
        print(f"Warning: Could not process image {image_path}. Error: {e}")
        return

if __name__ == "__main__":
    print(f"Executing main from {__file__}")
    print(f"Running base path: {BASE_PATH}")
    process_frames(BASE_PATH)