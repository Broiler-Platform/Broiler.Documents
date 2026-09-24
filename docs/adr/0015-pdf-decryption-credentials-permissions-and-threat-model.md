# ADR 0015: PDF Decryption - Credentials, Permissions, And Threat Model

**Status:** Accepted; the decisions it records were taken by the project reviewer
on 2026-09-24
**Date:** 2026-09-24

## Context

[ADR 0009](0009-pdf-security-resources-and-privacy.md) rejected encrypted input
outright and made any future encryption feature wait on "a separate ADR and
threat-model update". This is that ADR.

On 2026-09-24 the project reviewer supplied evidence records for the standard
security handler, the public-key security handler and ISO 32000-2, and approved
IP-015 (the standard handler at revisions 2, 3, 4 and 6), IP-002 (ISO 32000-2,
with feature-level review) and IP-025 (the public-key handler) on them. Six
decisions came with the approvals: revision 6 is in scope; the copy-and-extract
permission is enforced; a document whose user password is empty opens without
one, and a password-protected one with credentials; the roadmap §14.1 review
items are recorded rather than reviewed; encrypted output waits; and no new
test oracle is approved.

Investigating the work also found the old rejection weaker than ADR 0009 said.
Encryption was decided after the Catalog had been resolved. For a
cross-reference-stream file whose Catalog sits in an object stream, that meant
decoding the encrypted object stream and, when that failed, running a recovery
scan, all before the rejection. And with the cross-reference stream unreachable,
recovery rebuilt a trailer without `/Encrypt`, so the ciphertext was read as a
plain document that "needed OCR". Nothing leaked, since nothing was decrypted,
but ADR 0009's promise that recovery "cannot weaken encryption detection" did
not hold.

## Decision

**Read, never write.** The codec opens encrypted documents and encrypts none.
Encrypted output stays a roadmap §14.1 track; it would bring its own questions -
random initialization vectors against the writer's byte-identical output, a file
identifier needed before the body it is currently derived from - that reading
does not.

**Where it lives.** The standard security handler is in the base build, on the
LZW precedent (PDF extension points §1): its primitives are the runtime's but
RC4, which is written here, so nothing outside remains for the composition
boundary to hold. The public-key handler's PDF half - the recipient lists, the
key derivation, the permissions - is in the base build too; opening a CMS
envelope is not. That arrives through a composed `IPdfRecipientDecryptor`, and
no implementation ships, because the ready one is `System.Security.Cryptography.Pkcs`
and [ADR 0001](0001-component-topology-and-consumption-policy.md) keeps packages
that are not Broiler's out of every shipped assembly.

**The order.** Loading is three steps. The cross-reference data is discovered
and encryption decided from the trailers alone. The document is then opened -
authenticated, and its permissions checked - and only after that is anything
resolved that could be ciphertext: the Catalog, an object stream, a recovery
scan's contents. A document that cannot be opened, or may not be extracted from,
is rejected before any of them. Recovery keeps the promise ADR 0009 made for it:
a scan that stands in for the trailer reads the cross-reference streams'
dictionaries, which are never encrypted, and a file whose trailer is gone is
recognized by the one object shaped like an encryption dictionary.
Authentication then confirms the dictionary or refuses the file, so a wrong
guess cannot open anything.

**Credentials.** A password belongs to a document, so it travels in the read
options (`PdfReadOptions.WithCredentials`), never in the service graph. A
certificate recipient's key belongs to whoever the host runs for, so it travels
in the composed decryptor, and the codec never searches a certificate store.
Without credentials the empty password is tried, which is how a document
protected only by its permissions opens. The password is not a property, its
`ToString` withholds it, and no diagnostic carries it. Passing one is the
caller's statement that it may open the document with it; the codec cannot tell
whose password it was given.

**No prompt.** The codec performs no UI interaction (ADR 0009). A document that
needs a password is rejected with `pdf.encryption.password-required`, and a host
asks and reads again - the input is replayable. A wrong password is
`pdf.encryption.password-incorrect`, and the codec never falls back from a
supplied password to the empty one.

**Permissions.** Every output of this codec is extracted content: the text, the
model, what a render draws. So bit 5, copy and extract, is the one permission it
enforces itself. Without it, and without owner authority, the read is rejected
with `pdf.encryption.extraction-not-permitted`. The owner password lifts it, and
bit 10, accessibility extraction, does not, because this codec's output is not
limited to accessibility use. The other permissions are carried on
`PdfEncryptionInfo` for a host to honour - printing, modifying - and enforced by
nothing here. There is no switch to ignore bit 5. Adding one is a decision of its
own, and would reopen the anti-circumvention item IP-015 records.

**What comes out is not encrypted.** The document a read returns carries no
encryption. The `pdf.encryption.decrypted` diagnostic says that anything written
from it is written in the clear, and `PdfEncryptionInfo` tells a host the
document came from an encrypted file, so it can warn before a save.

**Boundaries.** Revision 5, an Adobe extension outside every approved record
(IP-003), is refused by name. So are `/V 3`, whose algorithm was never
published, and every handler but the two ISO 32000 defines. ISO 32000-2 is used
for revision 6 and nothing else (IP-002, feature-level).

## Threat model

| Threat | Response |
|---|---|
| Using the codec to guess passwords | A read tries the supplied password - in the few byte forms producers use - and the empty password, and nothing else. Nothing loops. A host that retries is the host's own policy |
| Work spent on key derivation as a denial of service | Revision 6's hash runs at least 64 rounds and stops by round 288 at the latest, charged to the read's work budget. Every other computation is a few hashes. RC4 and AES are linear in bytes the read already budgets |
| Plaintext reaching the caller of a refused read | A rejection carries no document, no metadata and no page count, and the tests check that neither the body nor the title reaches a diagnostic |
| A password reaching a log | It is not stored past the read, not echoed by any message, and not returned by `ToString`. The CLI takes it from a file, never an argument, so it reaches no process list and no shell history |
| Partial encryption (PDFex, CCS 2019): unencrypted objects placed in an encrypted file, to inject content or exfiltrate it | What the format lets a document declare unencrypted - `Identity` crypt filters, a metadata stream in the clear - is read and reported with `pdf.encryption.partially-unencrypted`, since anyone could have changed it. The exfiltration channels PDFex used are form submission, JavaScript and URIs opened without asking, and none exists here: no action is executed (ADR 0009), and a link is inert data the URI policy admits and a host activates only on a user gesture |
| CBC malleability, used to shape ciphertext that decrypts to a chosen URI | Same channel, same answer. A crafted link is admitted or refused like any other, and the garbage blocks such constructions leave behind are usually control characters the policy refuses. That is a mitigation, not a guarantee, and it is recorded as one |
| Permissions edited to grant more | At revisions 2 to 4, `/P` is an input to the file key, so an edited `/P` makes every password fail. Revision 6's key does not depend on `/P`, so it states the permissions again inside the encryption (`/Perms`); where the two disagree only what both grant is honoured, with `pdf.encryption.permissions-inconsistent` |
| Weak protection presented as strong | RC4 and 40-bit keys are read because documents made with them exist. The cipher and key length are stated in the `pdf.encryption.decrypted` diagnostic and on `PdfEncryptionInfo`, so a host can say what a document was protected with |
| Recovery weakening detection | Fixed as described under **The order**, with tests for an encrypted cross-reference-stream file whose `startxref` is broken, one that lost its trailer entirely, and an incremental update that introduces `/Encrypt` |

## Consequences

- A host - the Writer, today - opens documents protected only by permissions,
  where their permissions allow extraction, with no change of its own. For a
  password-protected document it needs a prompt keyed on
  `pdf.encryption.password-required` and `pdf.encryption.password-incorrect`.
- The feature matrix moves the handlers to `Candidate`. Nothing is `Supported`
  while SRC-025, the transcription of the security handlers' constants, is
  pending.
- ADR 0009's encryption bullet is amended for reading: encrypted input is opened
  as this ADR describes, and still never written.
- A public-key document needs a host that composes a decryptor, and the evidence
  for that handler is agreement with the tests' own envelopes rather than
  interoperability. IP-025 and the approved-sources log both say so.
