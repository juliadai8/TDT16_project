"""Download the original Middlebury DinoRing photographs and calibration."""
from pathlib import Path
from urllib.request import urlopen
from zipfile import ZipFile
import shutil
import tempfile

URL = "https://vision.middlebury.edu/mview/data/data/dinoRing.zip"
# Keep downloaded inputs outside Assets so Unity does not import the full dataset.
DEST = Path(__file__).resolve().parents[4] / "Reconstruction" / "data" / "dinoRing"


def main():
    if (DEST / "dinoR_par.txt").exists() and len(list(DEST.glob("dinoR*.png"))) == 48:
        print(f"Dataset is already available: {DEST}")
        return
    DEST.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryFile() as archive:
        with urlopen(URL, timeout=60) as response:
            shutil.copyfileobj(response, archive)
        archive.seek(0)
        with ZipFile(archive) as package:
            # Copy only the expected flat dataset files, avoiding archive paths.
            for item in package.infolist():
                name = Path(item.filename).name
                if item.is_dir() or not (name.startswith("dinoR") or name == "README.txt"):
                    continue
                with package.open(item) as source, (DEST / name).open("wb") as target:
                    shutil.copyfileobj(source, target)
    if len(list(DEST.glob("dinoR*.png"))) != 48 or not (DEST / "dinoR_par.txt").exists():
        raise RuntimeError("Download did not contain all 48 photos and calibration.")
    print(f"Downloaded 48 photos and camera calibration to {DEST}")


if __name__ == "__main__":
    main()
