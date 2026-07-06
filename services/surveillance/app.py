from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from surveillance_app.bootstrap import app, main


if __name__ == "__main__":
    main()
