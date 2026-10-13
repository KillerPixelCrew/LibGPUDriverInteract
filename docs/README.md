# LibGPUDriverInteract documentation

The library owns driver mechanisms. The consumer owns application identity, desired profiles,
presentation and when settings should be changed.

| Guide                                                       | Purpose                                                      |
| ----------------------------------------------------------- | ------------------------------------------------------------ |
| [Getting started](getting-started.md)                       | Build, discover vendors, subscribe and own a driver          |
| [Lifetime and events](lifecycle.md)                         | Native serialization, cancellation, topology and driver loss |
| [Controls](controls.md)                                     | Supported driver features, typed values and write outcomes   |
| [Application profiles](profiles.md)                         | Complete native sync sets and switched settings              |
| [Display identity](displays.md)                             | Physical monitors and changing Windows routes                |
| [Troubleshooting](troubleshooting.md)                       | Loading, availability, uncertainty and cleanup               |
| [API guide](../API.md) / [Complete reference](reference.md) | Public methods and contracts                                 |
| [Contributing](../CONTRIBUTING.md)                          | Native sources, support checks and regression cases          |

[Provenance](../PROVENANCE.md) identifies upstream sources and historical hardware observations.
Driver support is discovered at runtime; a catalog name does not guarantee that an installed driver
offers that feature.
