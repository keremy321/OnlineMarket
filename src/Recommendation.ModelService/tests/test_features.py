from __future__ import annotations

from decimal import Decimal
from uuid import UUID

from app.contracts import ProductTrainingSnapshot, UnitType
from app.features import build_product_document, normalize_net_content


def test_feature_construction_is_deterministic_and_normalized() -> None:
    product = ProductTrainingSnapshot(
        productId=UUID("00000000-0000-0000-0000-000000000001"),
        categoryId=UUID("10000000-0000-0000-0000-000000000001"),
        brandId=UUID("20000000-0000-0000-0000-000000000001"),
        name="  DARK   Coffee ",
        description="CAFÉ beans",
        unitType=UnitType.GRAM,
        netContent=Decimal("250.000"),
        price=Decimal("10.00"),
        isActive=True,
        isInStock=True,
    )

    first = build_product_document(product)
    second = build_product_document(product)

    assert first == second
    assert first.startswith("dark coffee café beans")
    assert "category_10000000000000000000000000000001" in first
    assert "brand_20000000000000000000000000000001" in first
    assert "unit_gram" in first
    assert "net_content_250" in first
    assert normalize_net_content(Decimal("1.500")) == "1_5"
