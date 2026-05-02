# /otel command reference

The canonical list. The README's "Commands" section embeds this
verbatim; `/otel help` prints this file unchanged. One source,
multiple surfaces.

```text
# /otel — bootstrap, master switch, persistent config
/otel                              setup-and-start; idempotent
/otel on                           collection enabled (this session)
/otel off                          collection paused (this session)
/otel status                       what's running, what's bound
/otel restart                      restart the collector binary
/otel help                         print this file
/otel set <key>:<value>            persistent enrichment (every session)
/otel get <key>                    read one persistent value (404 if unset)
/otel get <key1> <key2> ...        read several at once; always 200 + array
/otel unset <key>                  remove one persistent enrichment
/otel config                       show the persistent map
/otel config clear                 wipe the persistent map (confirms)
/otel extend [<topic>]             chain to /otel-extend (self-modify)

# /enrich — per-session enrichments (in-memory only)
/enrich <key> <value>              set
/enrich --remove <key>             remove one
/enrich --clear                    drop all
/enrich --show                     list current

# /weather — example skill, demonstrates the pattern
/weather                           current weather, IP-located
/weather <place>                   current weather for <place>
```
