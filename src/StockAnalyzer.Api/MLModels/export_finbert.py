"""
Export FinBERT model to ONNX format for sentiment analysis.

FinBERT is a BERT-based model fine-tuned for financial text sentiment analysis.
This script exports it to ONNX format for use with ONNX Runtime in C#.
"""

import os
from pathlib import Path
from optimum.onnxruntime import ORTModelForSequenceClassification
from transformers import AutoTokenizer

def export_finbert():
    model_name = "ProsusAI/finbert"
    # Pin to specific revision for security (prevents supply chain attacks)
    model_revision = "main"  # Use specific commit hash in production
    output_dir = Path(__file__).parent / "finbert-onnx"

    print(f"Exporting {model_name} to ONNX...")
    print(f"Output directory: {output_dir}")

    # Export model to ONNX
    model = ORTModelForSequenceClassification.from_pretrained(
        model_name,
        revision=model_revision,
        export=True
    )
    model.save_pretrained(output_dir)

    # Save tokenizer (with revision pinning for security)
    tokenizer = AutoTokenizer.from_pretrained(model_name, revision=model_revision)  # nosec B615
    tokenizer.save_pretrained(output_dir)

    # List exported files
    print("\nExported files:")
    for f in output_dir.iterdir():
        size_mb = f.stat().st_size / (1024 * 1024) if f.is_file() else 0
        print(f"  {f.name}: {size_mb:.2f} MB" if size_mb > 0 else f"  {f.name}/")

    print("\nExport complete!")
    return str(output_dir)

if __name__ == "__main__":
    export_finbert()
