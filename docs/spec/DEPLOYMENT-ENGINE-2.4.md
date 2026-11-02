# Deployment Engine 2.4

## State machine

```mermaid
stateDiagram-v2
    [*] --> FirstRun
    FirstRun --> Preflight
    Preflight --> PackageVerified
    PackageVerified --> Deployment
    Deployment --> Atak
    Atak --> Plugins
    Plugins --> Meshtastic
    Meshtastic --> Enrollment
    Enrollment --> Data
    Data --> OTS
    OTS --> FinalVerify
    FinalVerify --> Ready
    FinalVerify --> Recovery
    Recovery --> Deployment
    Ready --> [*]
```

## Rules

- explicit Deployment Policy wins over inference
- optional failures must not become required failures
- required failures block READY
- UNKNOWN is distinct from NOT READY
- Recovery resumes from verified state
- every completed stage remains auditable
