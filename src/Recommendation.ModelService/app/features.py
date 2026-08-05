from __future__ import annotations

import re
import unicodedata
from decimal import Decimal

from .contracts import ProductTrainingSnapshot

_WHITESPACE = re.compile(r"\s+")


def normalize_text(value: str | None) -> str:
    if not value:
        return ""
    normalized = unicodedata.normalize("NFKC", value).casefold()
    return _WHITESPACE.sub(" ", normalized).strip()


def normalize_net_content(value: Decimal) -> str:
    normalized = format(value.normalize(), "f")
    if "." in normalized:
        normalized = normalized.rstrip("0").rstrip(".")
    return normalized.replace("-", "minus_").replace(".", "_")


def build_product_document(product: ProductTrainingSnapshot) -> str:
    category_token = f"category_{product.categoryId.hex}"
    brand_token = f"brand_{product.brandId.hex}"
    unit_token = f"unit_{product.unitType.value.casefold()}"
    net_content_token = (
        f"net_content_{normalize_net_content(product.netContent)}"
    )
    return " ".join(
        part
        for part in (
            normalize_text(product.name),
            normalize_text(product.description),
            category_token,
            brand_token,
            unit_token,
            net_content_token,
        )
        if part
    )
