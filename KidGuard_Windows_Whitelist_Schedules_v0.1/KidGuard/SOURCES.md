# Technical references

Checked 2026-10-11. These describe platform behavior; they do not validate this implementation.

- Microsoft: per-user WFP condition uses a self-relative security descriptor and FWP_ACTRL_MATCH_FILTER — https://learn.microsoft.com/en-us/windows/win32/fwp/permitting-and-blocking-applications-and-users
- Microsoft: ALE layer connection semantics — https://learn.microsoft.com/en-us/windows/win32/fwp/ale-layers
- Microsoft: filter structure and persistent/boot-time distinction — https://learn.microsoft.com/en-us/windows/win32/api/fwpmtypes/ns-fwpmtypes-fwpm_filter0
- Microsoft: persistent provider and automatic service association — https://learn.microsoft.com/en-us/windows/win32/api/fwpmtypes/ns-fwpmtypes-fwpm_provider0
- Microsoft: filter arbitration, weights and permit/block interactions — https://learn.microsoft.com/en-us/windows/win32/fwp/filter-arbitration
- Microsoft: WFP tagged value types — https://learn.microsoft.com/en-us/windows/win32/api/fwptypes/ne-fwptypes-fwp_data_type
- Microsoft: local account flags through NetUserSetInfo — https://learn.microsoft.com/en-us/windows/win32/api/lmaccess/nf-lmaccess-netusersetinfo
- Microsoft: WTSLogoffSession — https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtslogoffsession
- Microsoft: Edge ECH policy — https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/encryptedclienthelloenabled
- Google: Chrome ECH policy — https://chromeenterprise.google/intl/en_ca/policies/encrypted-client-hello-enabled/
- Google: Chrome proxy mode — https://chromeenterprise.google/intl/en_ca/policies/proxy-mode/
- Mozilla: Firefox proxy policy — https://firefox-admin-docs.mozilla.org/reference/policies/proxy/
- Mozilla: Firefox DisableEncryptedClientHello (including GREASE caveat) — https://firefox-admin-docs.mozilla.org/reference/policies/disableencryptedclienthello/
- Mozilla: ECH and GREASE preferences — https://wiki.mozilla.org/index.php?title=Security/Encrypted_Client_Hello

Design choices such as five-second polling, a one-minute watchdog, strict TLS parsing, restricted destination addresses and the example timetable are this package's choices, not Microsoft/Mozilla guarantees.
