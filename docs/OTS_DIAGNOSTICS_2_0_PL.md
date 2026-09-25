# Diagnostyka OpenTAK Server

Field TAK Hub sprawdza:

- rozwiązywanie DNS hosta,
- TCP do portu API/enrollment,
- TCP do portu panelu/web,
- TCP do portu CoT.

Są to testy osiągalności sieciowej. Nie są one równoznaczne z potwierdzeniem uwierzytelnienia ATAK.

Na stockowym Androidzie aplikacja zewnętrzna nie ma wspieranego dostępu do prywatnego magazynu certyfikatów ATAK. Z tego powodu kontrola `Certyfikat ATAK` jest oznaczana jako `UNKNOWN / NIEZWERYFIKOWANE`, chyba że w przyszłości zostanie zastosowany zarządzany certyfikat znany Field TAK Hub lub oficjalne API/plugin ATAK umożliwiające taką telemetrię.
