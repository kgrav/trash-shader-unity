import os
import cv2

def create_video_from_images(image_folder, output_file, fps=30):
    # Get all image files in the folder
    images = [img for img in os.listdir(image_folder) if img.endswith((".png", ".jpg", ".jpeg"))]
    images.sort()  # Sort images alphabetically

    if not images:
        print("No images found in the folder.")
        return

    # Read the first image to get dimensions
    first_image_path = os.path.join(image_folder, images[0])
    frame = cv2.imread(first_image_path)
    height, width, layers = frame.shape

    # Define the codec and create VideoWriter object
    fourcc = cv2.VideoWriter_fourcc(*'mp4v')  # Codec for .mp4
    video = cv2.VideoWriter(output_file, fourcc, fps, (width, height))

    for image in images:
        image_path = os.path.join(image_folder, image)
        frame = cv2.imread(image_path)
        video.write(frame)

    video.release()
    print(f"Video saved as {output_file}")

if __name__ == "__main__":
    rendered_folder = "./.rendered"  # Path to the .rendered subfolder
    output_mp4 = "output_video.mp4"  # Output video file name
    create_video_from_images(rendered_folder, output_mp4)