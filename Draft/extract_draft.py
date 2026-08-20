from pathlib import Path
from pypdf import PdfReader

source = Path(r"d:\File Unity\Equilibrew\Draft\Draft Skripsi - Seminar - Stevanus Ryo Wijaya - 10122014.pdf")
destination = source.with_name("draft-skripsi.txt")
reader = PdfReader(str(source))
text = "\n\n".join(
    f"===== HALAMAN {index + 1} =====\n{page.extract_text() or ''}"
    for index, page in enumerate(reader.pages)
)
destination.write_text(text, encoding="utf-8")
print(f"Pages: {len(reader.pages)}")
print(f"Output: {destination}")
print(f"Characters: {len(text)}")
