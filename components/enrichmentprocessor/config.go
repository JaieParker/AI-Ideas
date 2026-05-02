// Copyright Jaie Parker
// SPDX-License-Identifier: Apache-2.0

package enrichmentprocessor

import "go.opentelemetry.io/collector/component"

// Config is the configuration for the enrichment processor.
//
// ExtensionID names the enrichmentctl extension this processor reads
// state from. The extension MUST be configured in the collector's
// service.extensions list, and it must already be Started before
// the processor's Start runs.
type Config struct {
	ExtensionID component.ID `mapstructure:"extension"`
}

func defaultConfig() *Config {
	return &Config{
		ExtensionID: component.MustNewID("enrichmentctl"),
	}
}
