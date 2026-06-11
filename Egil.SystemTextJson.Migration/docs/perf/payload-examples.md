# Benchmark Payload Examples

These examples show the JSON object shapes used by the small, medium, and large benchmark payload profiles. The small profile is intentionally tiny: it is the worst case for fixed library overhead because there is almost no user data to amortize the discriminator check over.

The medium profile is a best-guess average JSON object with about 12 object members across the root and one nested object. The large profile has about 96 object members spread across nested objects, arrays of objects, and dictionary entries.

| Payload size | Approx. object members | Labels | Items | History entries | Attributes | Text repetitions |
|--------------|-----------------------:|-------:|------:|----------------:|-----------:|-----------------:|
| Small | 2 | 0 | 0 | 0 | 0 | 0 |
| Medium | 12 | 3 | 0 | 0 | 0 | 2 |
| Large | 96 | 8 | 5 | 3 | 8 | 8 |

## Small

```json
{
  "name": "Jane Doe",
  "age": 42
}
```

## Medium

```json
{
  "name": "Jane Doe",
  "age": 42,
  "payload": {
    "status": "Processing",
    "isActive": true,
    "balance": 1200.50,
    "updatedAt": "2026-01-15T10:30:00+00:00",
    "contact": {
      "email": "jane.doe@example.net",
      "phone": "+45 12 34 56 78",
      "notes": "Contact note segment 00 contains representative JSON text. Contact note segment 01 contains representative JSON text."
    },
    "labels": [
      "label-000",
      "label-001",
      "label-002"
    ]
  }
}
```

## Large

```json
{
  "name": "Jane Doe",
  "age": 42,
  "payload": {
    "status": "Processing",
    "isActive": true,
    "balance": 1205.50,
    "updatedAt": "2026-01-18T10:30:00+00:00",
    "contact": {
      "email": "jane.doe@example.net",
      "phone": "+45 12 34 56 78",
      "notes": "Contact note segment 00 contains representative JSON text. ... Contact note segment 07 contains representative JSON text."
    },
    "labels": [
      "label-000",
      "label-001",
      "... 6 additional labels ..."
    ],
    "items": [
      {
        "sku": "SKU-00000",
        "description": "Line item 000 segment 00 contains representative JSON text. ... Line item 000 segment 07 contains representative JSON text.",
        "quantity": 1,
        "unitPrice": 9.95,
        "discountRate": 0.15,
        "isBackordered": true,
        "dimensions": {
          "weightKg": 0.5,
          "lengthCm": 10,
          "widthCm": 5,
          "heightCm": 3
        }
      },
      {
        "_omitted": "4 additional line items"
      }
    ],
    "history": [
      {
        "occurredAt": "2026-01-01T08:00:00+00:00",
        "actor": "user-00",
        "action": "Updated",
        "notes": "History note 000 segment 00 contains representative JSON text. ... History note 000 segment 07 contains representative JSON text.",
        "attempt": 1,
        "success": false
      },
      {
        "_omitted": "2 additional history entries"
      }
    ],
    "attributes": {
      "attribute-000": "Attribute value 000 segment 00 contains representative JSON text. ... Attribute value 000 segment 07 contains representative JSON text.",
      "_omitted": "7 additional attributes"
    }
  }
}
```
