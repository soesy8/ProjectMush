import bpy
import os
import sys


args = sys.argv[sys.argv.index("--") + 1 :]
source_path, output_path = args
image = bpy.data.images.load(source_path, check_existing=False)
image.filepath_raw = output_path
image.file_format = "PNG"
image.save()
print(f"REFERENCE_WRITTEN={output_path}")
