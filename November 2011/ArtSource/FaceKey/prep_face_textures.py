"""Cut the face out of reference.png and write the two face textures the key uses.

Run from anywhere:  python prep_face_textures.py   (needs Pillow)
Writes to Assets/Models/FaceKey/Textures/:
  FaceKey_Face_Albedo.png  - colour of the face (1024x1024)
  FaceKey_Face_Detail.png  - brightness-based detail map; build_face_key.py turns it into relief
"""
from pathlib import Path
from PIL import Image, ImageFilter

HERE = Path(__file__).resolve().parent
OUT = HERE.parent.parent / "Assets" / "Models" / "FaceKey" / "Textures"

# Face square in reference.png pixels (middle key). build_face_key.py uses the same numbers.
CX, CY, HALF = 630, 235, 128


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    src = Image.open(HERE / "reference.png").convert("RGB")
    face = src.crop((CX - HALF, CY - HALF, CX + HALF, CY + HALF)).resize((1024, 1024), Image.LANCZOS)
    face.save(OUT / "FaceKey_Face_Albedo.png")

    # Bright = raised (nose, lips, cheeks), dark = sunken (eyes, mouth line).
    lum = face.convert("L").resize((512, 512), Image.LANCZOS)
    broad = lum.filter(ImageFilter.GaussianBlur(18))   # big shapes
    fine = lum.filter(ImageFilter.GaussianBlur(1.5))   # wrinkles, lip edges
    Image.blend(broad, fine, 0.36).save(OUT / "FaceKey_Face_Detail.png")
    print("wrote", OUT)


if __name__ == "__main__":
    main()
