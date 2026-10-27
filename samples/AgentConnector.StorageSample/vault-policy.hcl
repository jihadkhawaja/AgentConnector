path "secret/data/agentconnector/*" {
  capabilities = ["create", "update", "read"]
}

path "secret/metadata/agentconnector/*" {
  capabilities = ["list", "delete"]
}
