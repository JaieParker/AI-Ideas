// Copyright Jaie Parker
// SPDX-License-Identifier: Apache-2.0

package enrichmentctlextension

// Config is the configuration for the enrichmentctl extension.
//
// Endpoint is the address the HTTP control API binds to (default
// 127.0.0.1:13133 — BR-OTEL-001 / BR-HELPERS-002 require loopback
// by default).
//
// PersistentFile is the path to the JSON file holding persistent
// enrichments (BR-ENRICH-009 — single source of truth). Loaded at
// startup; written through on POST /persistent-enrichments. v1
// uses simple os.WriteFile with O_TRUNC; atomic-rename hardening
// is a v2 follow-up.
type Config struct {
	Endpoint       string `mapstructure:"endpoint"`
	PersistentFile string `mapstructure:"persistent_file"`
}

func defaultConfig() *Config {
	return &Config{
		Endpoint:       "127.0.0.1:13133",
		PersistentFile: "persistent-enrichments.json",
	}
}
