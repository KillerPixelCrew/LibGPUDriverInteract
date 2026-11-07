# Driver channel fixtures

Recorded 2026-11-07 from official vendor responses (full responses retained by the WSGM host in
`artifacts/issue244-fix/channels/`). Intel HTML files retain the exact package metadata, SHA512 and
release-notes link from `www.intel.cn/content/www/cn/zh/download/`: product 785597 current 9175 WHQL,
revision 929959 (9034 Non-WHQL), revision 929557 (9033 WHQL), and product 864990 (7092 WHQL).
`intel-dsa.json` retains matching rows from `https://dsadata.intel.com/data/en` /
`software-configurations.json`; the DSA snapshot lacks 9175. The older-release channel scenario uses
the recorded 9034/9033 metadata pair and its matching certified selector option as a frozen page.
It does not claim 9034 is today's latest release.

AMD excerpts retain the exact 26.2.1 Optional and 26.1.1 WHQL articles from the RX 7800 XT official
previous-drivers page, the current 26.10.2 WHQL Optional article, and the exact selector JSON leaf.
NVIDIA is the recorded `DriverManualLookup` all-results response for psid=138, pfid=999, osID=135,
isWHQL=0, dch=1. It includes Studio records with IsWHQL=0 but WHQL filenames and contains no beta.
A separate test deliberately modifies IsBeta to exercise a future beta response; that mutation is
synthetic and never represented as a recorded release. All tests intercept HTTP and never install.
